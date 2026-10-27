import AppKit
let arguments = CommandLine.arguments
if arguments.contains("--self-test") {
    do { try runtimeSelfTests() } catch { fputs("\(error.localizedDescription)\n",stderr); exit(1) }
} else if arguments.contains("--prepare-only") || arguments.contains("--launch-cli") {
    do {
        let storage:URL
        if let i = arguments.firstIndex(of:"--runtime-root"), i + 1 < arguments.count { storage = URL(fileURLWithPath:arguments[i+1]) }
        else { storage = try defaultRuntimeStorage() }
        let manager = try RuntimeManager(payload:Bundle.main.resourceURL!.appendingPathComponent("Payload"),storage:storage)
        try ensureGameNotRunning(manager); try manager.acquireLock()
        let runtime = try manager.prepare(); print("Prepared runtime: " + runtime.path)
        if arguments.contains("--launch-cli") {
            let extra = arguments.firstIndex(of:"--").map{Array(arguments.dropFirst($0+1))} ?? []
            let child = try manager.launch(arguments:extra); child.waitUntilExit(); exit(child.terminationStatus)
        }
    } catch { fputs("\(error.localizedDescription)\n",stderr); exit(1) }
} else {
    let app = NSApplication.shared
    let delegate = GameLauncherDelegate(payload:Bundle.main.resourceURL!.appendingPathComponent("Payload"))
    app.delegate = delegate; app.setActivationPolicy(.regular); app.run()
}
