import Foundation
import CryptoKit
import Darwin

struct RuntimeFailure: LocalizedError {
    let message:String
    var errorDescription:String? { message }
}
struct RuntimeManifest: Codable {
    let format:Int
    let id:String
    let gameApp:String
    let byteCount:Int64
    let criticalHashes:[String:String]
    let requiredSizes:[String:Int64]
}
final class RuntimeManager {
    let manifest:RuntimeManifest
    let storage:URL
    let payload:URL
    private let manifestHash:String
    private let copy:(URL,URL)throws->Void
    private let availableBytes:(()throws->Int64)?
    private var lockFD:Int32 = -1
    private let fm = FileManager.default
    var runtimeURL:URL { storage.appendingPathComponent(manifest.id) }
    var logURL:URL { runtimeURL.appendingPathComponent("Launcher.log") }
    private struct Receipt:Codable { let format:Int; let id:String; let manifestHash:String }
    static func hash(_ data:Data)->String { SHA256.hash(data:data).map{String(format:"%02x",$0)}.joined() }
    private static func safeRelative(_ path:String)->Bool {
        !path.isEmpty && !path.hasPrefix("/") && !path.contains("\\") && !path.contains("\0") && path.split(separator:"/",omittingEmptySubsequences:false).allSatisfy{!$0.isEmpty && $0 != "." && $0 != ".."}
    }
    init(payload:URL,storage:URL,copy:((URL,URL)throws->Void)? = nil,availableBytes:(()throws->Int64)? = nil) throws {
        self.payload = payload; self.storage = storage; self.availableBytes = availableBytes
        let data = try Data(contentsOf:payload.appendingPathComponent("runtime.json"))
        manifestHash = Self.hash(data)
        manifest = try JSONDecoder().decode(RuntimeManifest.self,from:data)
        guard manifest.format == 1, !manifest.id.isEmpty, manifest.id.count <= 80,
              manifest.id.utf8.allSatisfy({ (48...57).contains($0) || (65...90).contains($0) || (97...122).contains($0) || $0 == 45 }),
              Self.safeRelative(manifest.gameApp), !manifest.gameApp.contains("/"), manifest.gameApp.hasSuffix(".app"),
              manifest.byteCount > 0, manifest.byteCount < 10_000_000_000_000,
              !manifest.criticalHashes.isEmpty,
              manifest.criticalHashes.keys.allSatisfy(Self.safeRelative), manifest.requiredSizes.keys.allSatisfy(Self.safeRelative),
              manifest.requiredSizes.values.allSatisfy({$0 >= 0}),
              manifest.criticalHashes.values.allSatisfy({$0.count == 64 && $0.utf8.allSatisfy{(48...57).contains($0) || (97...102).contains($0)}}) else {
            throw RuntimeFailure(message:"The bundled runtime manifest is invalid.")
        }
        self.copy = copy ?? { source,destination in
            let task = Process(); task.executableURL = URL(fileURLWithPath:"/usr/bin/ditto"); task.arguments = [source.path,destination.path]
            try task.run(); task.waitUntilExit()
            guard task.terminationStatus == 0 else { throw RuntimeFailure(message:"Game preparation failed while copying files. Free up space and try again.") }
        }
    }
    deinit { if lockFD >= 0 { _ = flock(lockFD,LOCK_UN); close(lockFD) } }
    private func checkNoLinks(_ url:URL) throws {
        var cursor = URL(fileURLWithPath:"/",isDirectory:true)
        for part in url.pathComponents.dropFirst() {
            cursor.appendPathComponent(part)
            do {
                let attributes = try fm.attributesOfItem(atPath:cursor.path)
                if attributes[.type] as? FileAttributeType == .typeSymbolicLink { throw RuntimeFailure(message:"A runtime path is a symbolic link: \(cursor.path)") }
            } catch let error as NSError where error.domain == NSCocoaErrorDomain && [NSFileNoSuchFileError,NSFileReadNoSuchFileError].contains(error.code) { return }
        }
    }
    func acquireLock() throws {
        if lockFD >= 0 { return }
        try checkNoLinks(storage)
        try fm.createDirectory(at:storage,withIntermediateDirectories:true)
        let path = storage.appendingPathComponent("launcher.lock")
        let fd = open(path.path,O_CREAT | O_RDWR | O_NOFOLLOW | O_CLOEXEC,mode_t(0o600))
        guard fd >= 0 else { throw RuntimeFailure(message:"Cannot create the runtime lock in \(storage.path).") }
        guard flock(fd,LOCK_EX | LOCK_NB) == 0 else { close(fd); throw RuntimeFailure(message:"TCG is already running or preparing its game files. Close it before opening another copy.") }
        lockFD = fd
    }
    private func validateFiles(_ directory:URL) throws {
        try checkNoLinks(directory)
        for (relative,size) in manifest.requiredSizes {
            let path = directory.appendingPathComponent(relative)
            try checkNoLinks(path)
            let attributes = try fm.attributesOfItem(atPath:path.path)
            guard attributes[.type] as? FileAttributeType == .typeRegular,
                  (attributes[.size] as? NSNumber)?.int64Value == size else { throw RuntimeFailure(message:"Required game/mod file is missing or changed: \(relative). Your runtime settings have been preserved.") }
        }
        for (relative,hash) in manifest.criticalHashes {
            let path = directory.appendingPathComponent(relative)
            try checkNoLinks(path)
            guard Self.hash(try Data(contentsOf:path)) == hash else { throw RuntimeFailure(message:"Required program file was changed: \(relative). Your runtime settings have been preserved.") }
        }
    }
    func prepare() throws -> URL {
        guard lockFD >= 0 else { throw RuntimeFailure(message:"Acquire the runtime lock before preparation.") }
        let destination = runtimeURL
        try checkNoLinks(destination)
        if fm.fileExists(atPath:destination.path) {
            let receiptURL = destination.appendingPathComponent(".prepared.json")
            try checkNoLinks(receiptURL)
            guard let data = try? Data(contentsOf:receiptURL), let receipt = try? JSONDecoder().decode(Receipt.self,from:data),
                  receipt.format == 1, receipt.id == manifest.id, receipt.manifestHash == manifestHash else {
                throw RuntimeFailure(message:"An incomplete or different runtime already exists at \(destination.path). It was left unchanged; move it aside before retrying.")
            }
            try validateFiles(destination)
            return destination
        }
        let source = payload.appendingPathComponent("Game")
        try validateFiles(source)
        let available = try availableBytes?() ?? (fm.attributesOfFileSystem(forPath:storage.path)[.systemFreeSize] as? NSNumber)?.int64Value ?? 0
        guard available >= manifest.byteCount + 536_870_912 else { throw RuntimeFailure(message:"First launch needs about \(Int(ceil(Double(manifest.byteCount)/1_073_741_824))) GB of free space for the game and mods. Free up space and try again.") }
        let staging = storage.appendingPathComponent("staging-" + UUID().uuidString)
        defer { if fm.fileExists(atPath:staging.path) { try? fm.removeItem(at:staging) } }
        try copy(source,staging)
        try validateFiles(staging)
        let receipt = Receipt(format:1,id:manifest.id,manifestHash:manifestHash)
        try JSONEncoder().encode(receipt).write(to:staging.appendingPathComponent(".prepared.json"),options:.atomic)
        try fm.moveItem(at:staging,to:destination)
        return destination
    }
    func launch(arguments:[String]) throws -> Process {
        let runtime = try prepare()
        let script = runtime.appendingPathComponent("run_bepinex.sh")
        guard fm.fileExists(atPath:script.path) else { throw RuntimeFailure(message:"The runtime launch script is missing.") }
        try checkNoLinks(logURL)
        let fd = open(logURL.path,O_WRONLY | O_CREAT | O_TRUNC | O_NOFOLLOW | O_CLOEXEC,mode_t(0o600))
        guard fd >= 0 else { throw RuntimeFailure(message:"Cannot write the launcher log at \(logURL.path).") }
        let output = FileHandle(fileDescriptor:fd,closeOnDealloc:true)
        let child = Process(); child.executableURL = URL(fileURLWithPath:"/bin/sh")
        child.currentDirectoryURL = runtime
        child.arguments = [script.path,runtime.appendingPathComponent(manifest.gameApp).path] + arguments
        child.standardOutput = output; child.standardError = output
        try child.run()
        return child
    }
}
