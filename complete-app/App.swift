import AppKit

func defaultRuntimeStorage() throws -> URL {
    try FileManager.default.url(for:.applicationSupportDirectory,in:.userDomainMask,appropriateFor:nil,create:false).appendingPathComponent("TCG Modded",isDirectory:true)
}
func ensureGameNotRunning(_ manager:RuntimeManager) throws {
    let app = manager.runtimeURL.appendingPathComponent(manager.manifest.gameApp).resolvingSymlinksInPath().path
    if NSWorkspace.shared.runningApplications.contains(where:{$0.bundleURL?.resolvingSymlinksInPath().path == app}) {
        throw RuntimeFailure(message:"The modded game is already running. Switch to its window to continue playing.")
    }
}
final class GameLauncherDelegate:NSObject,NSApplicationDelegate {
    let payload:URL
    var manager:RuntimeManager?
    var child:Process?
    var preparing = true
    var window:NSWindow!
    let detail = NSTextField(wrappingLabelWithString:"Preparing the native game and installed mods. Please leave this window open.")
    init(payload:URL) { self.payload = payload }
    func applicationDidFinishLaunching(_ notification:Notification) {
        let menu = NSMenu(); let appItem = NSMenuItem(); menu.addItem(appItem)
        let submenu = NSMenu(); submenu.addItem(withTitle:"Quit TCG Modded",action:#selector(NSApplication.terminate(_:)),keyEquivalent:"q"); appItem.submenu = submenu; NSApp.mainMenu = menu
        window = NSWindow(contentRect:NSRect(x:0,y:0,width:520,height:185),styleMask:[.titled],backing:.buffered,defer:false)
        window.title = "TCG Card Shop Simulator Modded"; window.center()
        let title = NSTextField(labelWithString:"Starting TCG Card Shop Simulator")
        title.font = .systemFont(ofSize:19,weight:.semibold)
        detail.textColor = .secondaryLabelColor
        let bar = NSProgressIndicator(); bar.style = .bar; bar.isIndeterminate = true; bar.startAnimation(nil)
        let stack = NSStackView(views:[title,detail,bar]); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 18; stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo:window.contentView!.leadingAnchor,constant:24),stack.trailingAnchor.constraint(equalTo:window.contentView!.trailingAnchor,constant:-24),stack.topAnchor.constraint(equalTo:window.contentView!.topAnchor,constant:24),bar.widthAnchor.constraint(equalToConstant:472),detail.widthAnchor.constraint(equalToConstant:472)])
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps:true)
        do {
            let runtime = try RuntimeManager(payload:payload,storage:defaultRuntimeStorage())
            try ensureGameNotRunning(runtime)
            try runtime.acquireLock()
            manager = runtime
            let exists = FileManager.default.fileExists(atPath:runtime.runtimeURL.path)
            detail.stringValue = exists ? "Checking your prepared game and mods…" : "Preparing game and mods for the first launch (about \(Int(ceil(Double(runtime.manifest.byteCount)/1_073_741_824))) GB). Later launches reuse these files."
            DispatchQueue.global(qos:.userInitiated).async { [self] in
                do {
                    _ = try runtime.prepare()
                    DispatchQueue.main.async { [self] in startGame(runtime) }
                } catch { DispatchQueue.main.async { [self] in fail(error) } }
            }
        } catch { fail(error) }
    }
    func startGame(_ runtime:RuntimeManager) {
        do {
            try ensureGameNotRunning(runtime)
            let game = try runtime.launch(arguments:[])
            child = game; preparing = false
            window.orderOut(nil); NSApp.setActivationPolicy(.accessory)
            DispatchQueue.global(qos:.utility).async { [self] in
                game.waitUntilExit()
                DispatchQueue.main.async { [self] in
                    child = nil
                    if game.terminationStatus == 0 { NSApp.terminate(nil) }
                    else { fail(RuntimeFailure(message:"The game stopped with exit code \(game.terminationStatus). Check Launcher.log and BepInEx/LogOutput.log in the runtime folder for details.")) }
                }
            }
        } catch { fail(error) }
    }
    func fail(_ error:Error) {
        preparing = false
        NSApp.setActivationPolicy(.regular); NSApp.activate(ignoringOtherApps:true)
        window?.orderOut(nil)
        let alert = NSAlert(); alert.messageText = "TCG could not start"; alert.informativeText = error.localizedDescription
        alert.addButton(withTitle:"Open Runtime Folder"); alert.addButton(withTitle:"Quit")
        if alert.runModal() == .alertFirstButtonReturn, let manager = manager { NSWorkspace.shared.open(manager.storage) }
        NSApp.terminate(nil)
    }
    func applicationShouldTerminate(_ sender:NSApplication)->NSApplication.TerminateReply {
        if preparing || child?.isRunning == true {
            if let child = child { NSRunningApplication(processIdentifier:child.processIdentifier)?.activate(options:[.activateAllWindows]) }
            else { window.makeKeyAndOrderFront(nil) }
            return .terminateCancel
        }
        return .terminateNow
    }
}
