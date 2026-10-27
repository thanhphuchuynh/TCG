import Foundation
import CryptoKit

struct GameInstallation {
    let root: URL
    let app: URL
}

struct InstallerFailure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}

final class InstallerCore {
    private let payload: URL
    private let isRunning: (GameInstallation) -> Bool
    private let fm = FileManager.default
    private let targetPath = "BepInEx/plugins/NativeShopMenu/NativeShopMenu.dll"
    private let statePath = ".tcg-native-installer"
    private struct Manifest: Decodable { let sha256: String; let sources: [String: String] }
    private struct Receipt: Codable {
        let format: Int
        let installedSHA256: String
        let originalSHA256: String?
        let createdPluginDirectory: Bool
    }

    init(payload: URL, isRunning: @escaping (GameInstallation) -> Bool = { _ in false }) {
        self.payload = Self.lexicalURL(payload)
        self.isRunning = isRunning
    }

    private static func lexicalURL(_ url: URL) -> URL {
        var parts: [String] = []
        for part in url.pathComponents where part != "/" && part != "." {
            if part == ".." { if !parts.isEmpty { parts.removeLast() } }
            else { parts.append(part) }
        }
        return URL(fileURLWithPath: "/" + parts.joined(separator: "/"))
    }

    private func fail(_ message: String) -> InstallerFailure { InstallerFailure(message: message) }
    static func sha256(_ data: Data) -> String { SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined() }
    private func validHash(_ hash: String) -> Bool { hash.count == 64 && hash.allSatisfy { "0123456789abcdef".contains($0) } }

    // Check every existing path component before reading or modifying it. Never follow links.
    private func inspect(_ url: URL, directory: Bool? = nil, required: Bool = true) throws {
        let path = Self.lexicalURL(url)
        var cursor = URL(fileURLWithPath: "/", isDirectory: true)
        for component in path.pathComponents.dropFirst() {
            cursor.appendPathComponent(component)
            let attrs: [FileAttributeKey: Any]
            do { attrs = try fm.attributesOfItem(atPath: cursor.path) }
            catch {
                if !required && ((error as NSError).domain == NSCocoaErrorDomain && [NSFileNoSuchFileError, NSFileReadNoSuchFileError].contains((error as NSError).code) || (error as NSError).domain == NSPOSIXErrorDomain && (error as NSError).code == 2) { return }
                throw fail("Missing or inaccessible path: \(cursor.path)")
            }
            guard let type = attrs[.type] as? FileAttributeType, type != .typeSymbolicLink else {
                throw fail("Symbolic links are not supported: \(cursor.path)")
            }
            if cursor != path && type != .typeDirectory { throw fail("Expected a directory: \(cursor.path)") }
            if cursor == path, let directory = directory {
                guard type == (directory ? .typeDirectory : .typeRegular) else { throw fail("Unexpected file type: \(cursor.path)") }
            }
        }
    }
    private func read(_ url: URL) throws -> Data { try inspect(url, directory: false); return try Data(contentsOf: url) }
    private func exists(_ url: URL) throws -> Bool {
        try inspect(url, required: false)
        return fm.fileExists(atPath: url.path)
    }
    private func child(_ root: URL, _ path: String) -> URL { root.appendingPathComponent(path) }

    func validate(selection: URL) throws -> GameInstallation {
        let selected = Self.lexicalURL(selection)
        try inspect(selected, directory: true)
        let app: URL
        if selected.pathExtension.lowercased() == "app" { app = selected }
        else {
            let candidates = try fm.contentsOfDirectory(at: selected, includingPropertiesForKeys: nil)
                .filter { $0.pathExtension.lowercased() == "app" && fm.fileExists(atPath: child($0, "Contents/Resources/Data/Managed/Assembly-CSharp.dll").path) }
            guard candidates.count == 1 else { throw fail("Choose the TCG Card Shop Simulator app, or a folder containing exactly one copy.") }
            app = candidates[0]
        }
        try inspect(app, directory: true)
        let game = GameInstallation(root: app.deletingLastPathComponent(), app: app)
        _ = try read(child(app, "Contents/Resources/Data/Managed/Assembly-CSharp.dll"))
        let plist = try PropertyListSerialization.propertyList(from: read(child(app, "Contents/Info.plist")), format: nil) as? [String: Any]
        guard let executable = plist?["CFBundleExecutable"] as? String,
              !executable.isEmpty, executable != ".", executable != "..",
              !executable.contains("/"), !executable.contains("\\"), !executable.contains("\0") else {
            throw fail("The app has no safe CFBundleExecutable entry.")
        }
        guard Self.hasARM64MachO(try read(child(app, "Contents/MacOS/" + executable))) else {
            throw fail("Select a native macOS app with an arm64 Mach-O executable.")
        }
        for dependency in ["core/BepInEx.dll", "core/0Harmony.dll", "plugins/ShopOS/ShopOS.dll", "plugins/EnhancedPrefabLoader/EnhancedPrefabLoader.dll"] {
            try inspect(child(game.root, "BepInEx/" + dependency), directory: false)
        }
        try inspect(child(game.root, targetPath), directory: false, required: false)
        try inspect(child(game.root, statePath), directory: true, required: false)
        return game
    }

    private static func hasARM64MachO(_ data: Data) -> Bool {
        func u32(_ offset: Int, _ little: Bool = false) -> UInt32? {
            guard offset >= 0, offset <= data.count - 4 else { return nil }
            let b = Array(data[offset..<offset+4])
            return little ? UInt32(b[0]) | UInt32(b[1]) << 8 | UInt32(b[2]) << 16 | UInt32(b[3]) << 24 : UInt32(b[3]) | UInt32(b[2]) << 8 | UInt32(b[1]) << 16 | UInt32(b[0]) << 24
        }
        func thin(_ offset: Int, _ available: Int) -> Bool {
            guard available >= 32, let magic = u32(offset), magic == 0xcffaedfe || magic == 0xfeedfacf else { return false }
            return u32(offset + 4, magic == 0xcffaedfe) == 0x0100000c && u32(offset + 12, magic == 0xcffaedfe) == 2
        }
        if thin(0, data.count) { return true }
        guard let magic = u32(0), [UInt32(0xcafebabe), 0xbebafeca, 0xcafebabf, 0xbfbafeca].contains(magic), let count = u32(4, magic == 0xbebafeca || magic == 0xbfbafeca), count > 0, count <= 64 else { return false }
        let little = magic == 0xbebafeca || magic == 0xbfbafeca
        let wide = magic == 0xcafebabf || magic == 0xbfbafeca
        let stride = wide ? 32 : 20
        guard data.count >= 8 + Int(count) * stride else { return false }
        for index in 0..<Int(count) {
            let base = 8 + index * stride
            guard u32(base, little) == 0x0100000c else { continue }
            // Files larger than 4 GiB are intentionally unsupported.
            let offset: UInt32? = wide ? (u32(base + (little ? 12 : 8), little) == 0 ? u32(base + (little ? 8 : 12), little) : nil) : u32(base+8, little)
            let size: UInt32? = wide ? (u32(base + (little ? 20 : 16), little) == 0 ? u32(base + (little ? 16 : 20), little) : nil) : u32(base+12, little)
            if let offset = offset, let size = size, Int(offset) <= data.count, Int(size) <= data.count - Int(offset), thin(Int(offset), Int(size)) { return true }
        }
        return false
    }

    private func checkedGame(_ game: GameInstallation) throws -> GameInstallation {
        let checked = try validate(selection: game.app)
        guard Self.lexicalURL(checked.root).path == Self.lexicalURL(game.root).path else { throw fail("The selected game folder changed.") }
        guard !isRunning(checked) else { throw fail("Quit the game before installing or restoring.") }
        return checked
    }
    private func receipt(_ state: URL) throws -> Receipt {
        let entries = try fm.contentsOfDirectory(atPath: state.path)
        guard Set(entries).isSubset(of: ["receipt.json", "original.dll"]), entries.contains("receipt.json") else { throw fail("Installer state is incomplete or contains unexpected files.") }
        let result = try JSONDecoder().decode(Receipt.self, from: read(child(state, "receipt.json")))
        guard result.format == 1, validHash(result.installedSHA256), result.originalSHA256.map(validHash) ?? true else { throw fail("Invalid installer receipt.") }
        if let hash = result.originalSHA256 {
            guard Self.sha256(try read(child(state, "original.dll"))) == hash else { throw fail("The original backup was modified; restore stopped.") }
        } else if entries.contains("original.dll") { throw fail("Unexpected original backup.") }
        return result
    }
    private func write(_ data: Data, _ url: URL) throws {
        try inspect(url.deletingLastPathComponent(), directory: true)
        try inspect(url, directory: false, required: false)
        try data.write(to: url, options: .atomic)
    }

    func install(_ selected: GameInstallation) throws -> String {
        let game = try checkedGame(selected)
        let manifest = try JSONDecoder().decode(Manifest.self, from: read(child(payload, "manifest.json")))
        let dll = try read(child(payload, "NativeShopMenu.dll"))
        guard validHash(manifest.sha256), !manifest.sources.isEmpty, manifest.sources.values.allSatisfy(validHash), Self.sha256(dll) == manifest.sha256 else { throw fail("The installer payload failed its SHA-256 check.") }
        let target = child(game.root, targetPath), directory = target.deletingLastPathComponent(), state = child(game.root, statePath)
        let oldDLL = try exists(target) ? read(target) : nil
        let hadDirectory = try exists(directory), hadState = try exists(state)
        let previousReceipt: Data?
        let originalHash: String?
        let createdDirectory: Bool
        if hadState {
            let previous = try receipt(state)
            guard oldDLL.map(Self.sha256) == previous.installedSHA256 else { throw fail("The installed DLL was edited or removed; installation stopped.") }
            if previous.installedSHA256 == manifest.sha256 { return "This version is already installed." }
            previousReceipt = try read(child(state, "receipt.json"))
            originalHash = previous.originalSHA256
            createdDirectory = previous.createdPluginDirectory
        } else {
            previousReceipt = nil
            originalHash = oldDLL.map(Self.sha256)
            createdDirectory = !hadDirectory
        }
        do {
            if !hadState {
                try fm.createDirectory(at: state, withIntermediateDirectories: false)
                if let oldDLL = oldDLL { try write(oldDLL, child(state, "original.dll")) }
            }
            if !hadDirectory { try fm.createDirectory(at: directory, withIntermediateDirectories: false) }
            try write(dll, target)
            let next = Receipt(format: 1, installedSHA256: manifest.sha256, originalSHA256: originalHash, createdPluginDirectory: createdDirectory)
            try write(JSONEncoder().encode(next), child(state, "receipt.json"))
        } catch {
            do {
                if let oldDLL = oldDLL {
                    if (try? read(target)) != oldDLL { try write(oldDLL, target) }
                }
                else if try exists(target) { try fm.removeItem(at: target) }
                if !hadDirectory, try exists(directory), try fm.contentsOfDirectory(atPath: directory.path).isEmpty { try fm.removeItem(at: directory) }
                if let previousReceipt = previousReceipt { try write(previousReceipt, child(state, "receipt.json")) }
                else if try exists(state) { try fm.removeItem(at: state) }
            } catch { throw fail("Installation failed and rollback needs attention. Preserve \(state.path). \(error.localizedDescription)") }
            throw error
        }
        return "NativeShopMenu installed. Your saves and other mods were not changed."
    }

    func restore(_ selected: GameInstallation) throws -> String {
        let game = try checkedGame(selected), state = child(selected.root, statePath)
        guard try exists(state) else { return "There is no installer backup to restore." }
        let record = try receipt(state), target = child(game.root, targetPath)
        let installed = try read(target)
        guard Self.sha256(installed) == record.installedSHA256 else { throw fail("The installed DLL was edited; restore stopped.") }
        let original = try record.originalSHA256.map { _ in try read(child(state, "original.dll")) }
        do {
            if let original = original { try write(original, target) }
            else { try fm.removeItem(at: target) }
            // Renaming detaches the whole recovery state atomically; cleanup cannot leave a partial active receipt.
            let retired = child(game.root, ".tcg-native-installer-restored-" + UUID().uuidString)
            try fm.moveItem(at: state, to: retired)
            try? fm.removeItem(at: retired)
        } catch {
            do { if (try? read(target)) != installed { try write(installed, target) } }
            catch { throw fail("Restore failed and rollback needs attention. Preserve \(state.path). \(error.localizedDescription)") }
            throw error
        }
        if record.createdPluginDirectory {
            let directory = target.deletingLastPathComponent()
            if (try? fm.contentsOfDirectory(atPath: directory.path).isEmpty) == true { try? fm.removeItem(at: directory) }
        }
        return "Restored the state from before the first installation. Your saves were not changed."
    }
}
