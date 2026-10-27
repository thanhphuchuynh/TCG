import AppKit
if CommandLine.arguments.contains("--self-test") {
    do { try runInstallerTests() } catch { fputs("Installer tests failed: \(error)\n", stderr); exit(1) }
} else if let index = CommandLine.arguments.firstIndex(of: "--check-game"), index + 1 < CommandLine.arguments.count {
    do {
        let core = InstallerCore(payload: Bundle.main.resourceURL!.appendingPathComponent("Payload"))
        let game = try core.validate(selection: URL(fileURLWithPath: CommandLine.arguments[index+1]))
        print("Compatible game structure: " + game.root.path)
    } catch { fputs("Validation failed: \(error.localizedDescription)\n", stderr); exit(1) }
} else {
    let app = NSApplication.shared
    let delegate = InstallerDelegate()
    app.delegate = delegate
    app.setActivationPolicy(.regular)
    app.run()
}
