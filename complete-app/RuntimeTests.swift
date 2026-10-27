import Foundation
import CryptoKit

func runtimeSelfTests() throws {
    let fm = FileManager.default
    let suite = URL(fileURLWithPath: "/private/tmp/TCGCompleteTests-" + UUID().uuidString)
    try fm.createDirectory(at: suite, withIntermediateDirectories:true)
    defer { try? fm.removeItem(at: suite) }
    var count = 0
    func expect(_ ok: @autoclosure () throws -> Bool, _ message:String) throws {
        guard try ok() else { throw RuntimeFailure(message:"Test failed: " + message) }; count += 1
    }
    func rejects(_ message:String, _ body:()throws->Void) throws {
        do { try body() } catch { count += 1; return }
        throw RuntimeFailure(message:"Expected rejection: " + message)
    }
    func put(_ string:String, _ url:URL) throws {
        try fm.createDirectory(at:url.deletingLastPathComponent(), withIntermediateDirectories:true)
        try Data(string.utf8).write(to:url)
    }
    let payload = suite.appendingPathComponent("Payload with spaces")
    let game = payload.appendingPathComponent("Game")
    try put("game-binary",game.appendingPathComponent("Test Game.app/Contents/MacOS/Player"))
    try put("plugin",game.appendingPathComponent("BepInEx/plugins/Example.dll"))
    try put("setting=original",game.appendingPathComponent("BepInEx/config/Example.cfg"))
    try put("#!/bin/sh\nprintf '%s\\n' \"$@\" > arguments.txt\n",game.appendingPathComponent("run_bepinex.sh"))
    func digest(_ path:URL) throws -> String { SHA256.hash(data:try Data(contentsOf:path)).map{String(format:"%02x",$0)}.joined() }
    let critical = ["Test Game.app/Contents/MacOS/Player", "BepInEx/plugins/Example.dll", "run_bepinex.sh"]
    let hashes = try Dictionary(uniqueKeysWithValues:critical.map{($0,try digest(game.appendingPathComponent($0)))})
    let manifest:[String:Any] = ["format":1,"id":"test-v1","gameApp":"Test Game.app","byteCount":1024,"criticalHashes":hashes,"requiredSizes":["Test Game.app/Contents/MacOS/Player":11]]
    try JSONSerialization.data(withJSONObject:manifest,options:[.sortedKeys]).write(to:payload.appendingPathComponent("runtime.json"))
    let storage = suite.appendingPathComponent("Application Support with spaces")
    var manager:RuntimeManager? = try RuntimeManager(payload:payload,storage:storage)
    try manager!.acquireLock()
    let runtime = try manager!.prepare()
    try expect(try Data(contentsOf:runtime.appendingPathComponent("BepInEx/plugins/Example.dll")) == Data("plugin".utf8),"first prepare copies plugin")
    try expect(runtime.path.hasPrefix(storage.path),"runtime lives outside bundle")
    try expect(fm.fileExists(atPath:runtime.appendingPathComponent(".prepared.json").path),"ready receipt written after preparation")
    try put("setting=changed",runtime.appendingPathComponent("BepInEx/config/Example.cfg"))
    try expect(try manager!.prepare().path == runtime.path,"repeat returns same runtime")
    try expect(try String(contentsOf:runtime.appendingPathComponent("BepInEx/config/Example.cfg"),encoding:.utf8) == "setting=changed","repeat preserves changed configuration")
    var contender:RuntimeManager? = try RuntimeManager(payload:payload,storage:storage)
    try rejects("duplicate launch lock") { try contender!.acquireLock() }
    let child = try manager!.launch(arguments:["two words","$(literal)","line\nbreak"])
    child.waitUntilExit()
    try expect(child.terminationStatus == 0,"launch script completes")
    let arguments = try String(contentsOf:runtime.appendingPathComponent("arguments.txt"),encoding:.utf8)
    try expect(arguments == runtime.appendingPathComponent("Test Game.app").path + "\ntwo words\n$(literal)\nline\nbreak\n","arguments preserve spaces and shell characters")
    try expect(try String(contentsOf:game.appendingPathComponent("BepInEx/config/Example.cfg"),encoding:.utf8) == "setting=original","bundle payload stays unchanged")
    manager = nil
    try contender!.acquireLock()
    try expect(true,"lock released when manager ends")
    contender = nil
    let movedPayload = suite.appendingPathComponent("Moved App Payload")
    try fm.copyItem(at:payload,to:movedPayload)
    let moved = try RuntimeManager(payload:movedPayload,storage:storage)
    try moved.acquireLock()
    try expect(try moved.prepare().path == runtime.path,"moving app reuses existing runtime")
    try expect(try String(contentsOf:runtime.appendingPathComponent("BepInEx/config/Example.cfg"),encoding:.utf8) == "setting=changed","moving app preserves configuration")
    let interruptedStorage = suite.appendingPathComponent("interrupted")
    let interrupted = try RuntimeManager(payload:payload,storage:interruptedStorage,copy:{ _,destination in
        try fm.createDirectory(at:destination,withIntermediateDirectories:true)
        try put("partial",destination.appendingPathComponent("partial"))
        throw RuntimeFailure(message:"simulated copy failure")
    })
    try interrupted.acquireLock()
    try rejects("copy failure") { _ = try interrupted.prepare() }
    try expect(!(try fm.contentsOfDirectory(atPath:interruptedStorage.path)).contains(where:{$0.hasPrefix("staging-") || $0 == "test-v1"}),"partial copy is cleaned, not marked ready")
    let low = try RuntimeManager(payload:payload,storage:suite.appendingPathComponent("low"),availableBytes:{0})
    try low.acquireLock(); try rejects("insufficient disk space") { _ = try low.prepare() }
    try put("edited",runtime.appendingPathComponent("BepInEx/plugins/Example.dll"))
    try rejects("edited immutable plugin") { _ = try moved.prepare() }
    try expect(try String(contentsOf:runtime.appendingPathComponent("BepInEx/config/Example.cfg"),encoding:.utf8) == "setting=changed","integrity refusal preserves settings")
    try rejects("missing payload") { _ = try RuntimeManager(payload:suite.appendingPathComponent("missing"),storage:suite.appendingPathComponent("unused")) }
    var unsafe = manifest; unsafe["id"] = "../escape"
    try JSONSerialization.data(withJSONObject:unsafe).write(to:payload.appendingPathComponent("runtime.json"))
    try rejects("unsafe runtime ID") { _ = try RuntimeManager(payload:payload,storage:suite.appendingPathComponent("unsafe")) }
    try JSONSerialization.data(withJSONObject:manifest,options:[.sortedKeys]).write(to:payload.appendingPathComponent("runtime.json"))
    let linked = suite.appendingPathComponent("linked")
    try fm.createSymbolicLink(at:linked,withDestinationURL:storage)
    try rejects("linked storage") { let m = try RuntimeManager(payload:payload,storage:linked); try m.acquireLock() }
    let unreadyStorage = suite.appendingPathComponent("unready")
    try put("personal",unreadyStorage.appendingPathComponent("test-v1/personal.txt"))
    let unready = try RuntimeManager(payload:payload,storage:unreadyStorage); try unready.acquireLock()
    try rejects("existing unready runtime") { _ = try unready.prepare() }
    try expect(fm.fileExists(atPath:unreadyStorage.appendingPathComponent("test-v1/personal.txt").path),"unready folder not overwritten")
    print("Complete-app runtime tests passed (\(count) assertions).")
}
