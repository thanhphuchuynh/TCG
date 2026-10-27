import Foundation

func runInstallerTests() throws {
    let fm = FileManager.default
    let suite = URL(fileURLWithPath: "/private/tmp").appendingPathComponent("TCGInstallerTests-" + UUID().uuidString)
    try fm.createDirectory(at: suite, withIntermediateDirectories: true)
    defer { try? fm.removeItem(at: suite) }
    var assertions = 0
    func check(_ condition: @autoclosure () throws -> Bool, _ message: String) throws {
        guard try condition() else { throw InstallerFailure(message: "Self-test failed: " + message) }
        assertions += 1
    }
    func rejects(_ message: String, _ action: () throws -> Void) throws {
        do { try action() }
        catch { assertions += 1; return }
        throw InstallerFailure(message: "Self-test expected rejection: " + message)
    }
    func put(_ data: Data, _ url: URL) throws {
        try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try data.write(to: url)
    }
    func fixture(_ name: String) throws -> (URL, URL, URL) {
        let root = suite.appendingPathComponent(name), app = root.appendingPathComponent("TCG Card Shop Simulator.app")
        try put(Data("dummy game".utf8), app.appendingPathComponent("Contents/Resources/Data/Managed/Assembly-CSharp.dll"))
        let plist = try PropertyListSerialization.data(fromPropertyList: ["CFBundleExecutable": "TCG"], format: .xml, options: 0)
        try put(plist, app.appendingPathComponent("Contents/Info.plist"))
        var macho = Data([0xcf, 0xfa, 0xed, 0xfe, 0x0c, 0, 0, 1, 0, 0, 0, 0, 2, 0, 0, 0])
        macho.append(Data(repeating: 0, count: 16))
        try put(macho, app.appendingPathComponent("Contents/MacOS/TCG"))
        for path in ["core/BepInEx.dll", "core/0Harmony.dll", "plugins/ShopOS/ShopOS.dll", "plugins/EnhancedPrefabLoader/EnhancedPrefabLoader.dll"] {
            try put(Data("dummy dependency".utf8), root.appendingPathComponent("BepInEx/" + path))
        }
        let payload = root.appendingPathComponent("payload")
        try setPayload(payload, "release-one")
        return (root, app, payload)
    }
    func setPayload(_ payload: URL, _ version: String) throws {
        let dll = Data(version.utf8)
        try put(dll, payload.appendingPathComponent("NativeShopMenu.dll"))
        try put(JSONSerialization.data(withJSONObject: ["sha256": InstallerCore.sha256(dll), "sources": ["NativeShopMenu.cs": InstallerCore.sha256(Data("source".utf8))]]), payload.appendingPathComponent("manifest.json"))
    }
    let (root, app, payload) = try fixture("lifecycle")
    let core = InstallerCore(payload: payload), target = root.appendingPathComponent("BepInEx/plugins/NativeShopMenu/NativeShopMenu.dll")
    let game = try core.validate(selection: root)
    try check(game.app.path == app.path, "parent folder selects game")
    _ = try core.validate(selection: app)
    let save = root.appendingPathComponent("save-sentinel.dat")
    try put(Data("save untouched".utf8), save)
    _ = try core.install(game)
    try check(try Data(contentsOf: target) == Data("release-one".utf8), "installed payload")
    let receipt = root.appendingPathComponent(".tcg-native-installer/receipt.json")
    let firstReceipt = try Data(contentsOf: receipt)
    _ = try core.install(game)
    try check(try Data(contentsOf: receipt) == firstReceipt, "same-version install preserves receipt")
    try setPayload(payload, "release-two")
    _ = try core.install(game)
    try check(try Data(contentsOf: target) == Data("release-two".utf8), "update replaces payload")
    _ = try core.restore(game)
    try check(!fm.fileExists(atPath: target.path), "restore removes newly installed DLL")
    try check(!fm.fileExists(atPath: target.deletingLastPathComponent().path), "restore removes installer-created empty directory")
    try check(try Data(contentsOf: save) == Data("save untouched".utf8), "save sentinel untouched")
    _ = try core.restore(game)
    try put(Data("original user DLL".utf8), target)
    _ = try core.install(game)
    try setPayload(payload, "release-three")
    _ = try core.install(game)
    _ = try core.install(game)
    _ = try core.restore(game)
    try check(try Data(contentsOf: target) == Data("original user DLL".utf8), "update and idempotence preserve first original")
    _ = try core.install(game)
    try put(Data("edited installed DLL".utf8), target)
    try rejects("edited target restore") { _ = try core.restore(game) }
    try rejects("edited target install") { _ = try core.install(game) }
    try check(try Data(contentsOf: target) == Data("edited installed DLL".utf8), "tamper refusal leaves file intact")
    try put(Data("release-three".utf8), target)
    let backup = root.appendingPathComponent(".tcg-native-installer/original.dll")
    try put(Data("edited backup".utf8), backup)
    try rejects("edited backup") { _ = try core.restore(game) }
    let (missingRoot, missingApp, missingPayload) = try fixture("missing")
    let missingCore = InstallerCore(payload: missingPayload)
    try fm.removeItem(at: missingRoot.appendingPathComponent("BepInEx/core/0Harmony.dll"))
    try rejects("missing dependency") { _ = try missingCore.validate(selection: missingApp) }
    let (unsafeRoot, unsafeApp, unsafePayload) = try fixture("unsafe")
    let unsafeCore = InstallerCore(payload: unsafePayload)
    let unsafeGame = try unsafeCore.validate(selection: unsafeApp)
    try rejects("game running") { _ = try InstallerCore(payload: unsafePayload, isRunning: { _ in true }).install(unsafeGame) }
    try put(Data("bad payload".utf8), unsafePayload.appendingPathComponent("NativeShopMenu.dll"))
    try rejects("payload hash mismatch") { _ = try unsafeCore.install(unsafeGame) }
    try setPayload(unsafePayload, "release-one")
    let external = suite.appendingPathComponent("outside")
    try fm.createDirectory(at: external, withIntermediateDirectories: false)
    let linkDir = unsafeRoot.appendingPathComponent("BepInEx/plugins/NativeShopMenu")
    try fm.createSymbolicLink(at: linkDir, withDestinationURL: external)
    try rejects("plugin symlink escape") { _ = try unsafeCore.install(unsafeGame) }
    try fm.removeItem(at: linkDir)
    let state = unsafeRoot.appendingPathComponent(".tcg-native-installer")
    try fm.createSymbolicLink(at: state, withDestinationURL: external)
    try rejects("state symlink escape") { _ = try unsafeCore.install(unsafeGame) }
    try fm.removeItem(at: state)
    _ = try unsafeCore.install(unsafeGame)
    let unsafeReceipt = state.appendingPathComponent("receipt.json")
    try fm.removeItem(at: unsafeReceipt)
    let externalFile = external.appendingPathComponent("receipt.json")
    try put(Data("untouched".utf8), externalFile)
    try fm.createSymbolicLink(at: unsafeReceipt, withDestinationURL: externalFile)
    try rejects("receipt symlink escape") { _ = try unsafeCore.restore(unsafeGame) }
    try check(try Data(contentsOf: externalFile) == Data("untouched".utf8), "symlink target untouched")
    try fm.removeItem(at: unsafeApp.appendingPathComponent("Contents/MacOS/TCG"))
    try put(Data("Windows executable".utf8), unsafeApp.appendingPathComponent("Contents/MacOS/TCG"))
    try rejects("non-MachO executable") { _ = try unsafeCore.validate(selection: unsafeApp) }
    let (archRoot, archApp, archPayload) = try fixture("architectures")
    let archCore = InstallerCore(payload: archPayload)
    let executable = archApp.appendingPathComponent("Contents/MacOS/TCG")
    let armSlice = try Data(contentsOf: executable)
    var universal = Data([0xca, 0xfe, 0xba, 0xbe, 0, 0, 0, 1, 1, 0, 0, 0x0c, 0, 0, 0, 0, 0, 0, 0, 28, 0, 0, 0, 32, 0, 0, 0, 0])
    universal.append(armSlice)
    try put(universal, executable)
    _ = try archCore.validate(selection: archApp)
    assertions += 1
    var intel = armSlice
    intel[4] = 7
    try put(intel, executable)
    try rejects("x86-only executable") { _ = try archCore.validate(selection: archApp) }
    try put(armSlice, executable)
    let info = archApp.appendingPathComponent("Contents/Info.plist")
    let originalInfo = try Data(contentsOf: info)
    try put(PropertyListSerialization.data(fromPropertyList: ["CFBundleExecutable": "../escape"], format: .xml, options: 0), info)
    try rejects("unsafe executable filename") { _ = try archCore.validate(selection: archApp) }
    try put(originalInfo, info)
    let archGame = try archCore.validate(selection: archApp)
    let archTarget = archRoot.appendingPathComponent("BepInEx/plugins/NativeShopMenu/NativeShopMenu.dll")
    try put(Data("original".utf8), archTarget)
    let archDirectory = archTarget.deletingLastPathComponent()
    try fm.setAttributes([.posixPermissions: 0o555], ofItemAtPath: archDirectory.path)
    do {
        defer { try? fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: archDirectory.path) }
        try rejects("atomic target write failure") { _ = try archCore.install(archGame) }
        try check(try Data(contentsOf: archTarget) == Data("original".utf8), "failed write preserves original")
        // A rollback must not need to rewrite an unchanged file in an unwritable directory.
        try check(!fm.fileExists(atPath: archRoot.appendingPathComponent(".tcg-native-installer").path), "failed installation removes new recovery state")
    }
    _ = try archCore.install(archGame)
    try fm.setAttributes([.posixPermissions: 0o555], ofItemAtPath: archRoot.path)
    do {
        defer { try? fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: archRoot.path) }
        try rejects("restore recovery-state rename failure") { _ = try archCore.restore(archGame) }
        try check(try Data(contentsOf: archTarget) == Data("release-one".utf8), "failed restore rolls target back")
        try check(fm.fileExists(atPath: archRoot.appendingPathComponent(".tcg-native-installer/receipt.json").path), "failed restore retains receipt")
    }
    _ = try archCore.restore(archGame)
    try check(try Data(contentsOf: archTarget) == Data("original".utf8), "retry restores original after rename failure")
    print("Installer self-tests passed (\(assertions) assertions). Fixtures contained dummy files only.")
}
