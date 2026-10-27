import AppKit

final class InstallerDelegate: NSObject, NSApplicationDelegate {
    var window: NSWindow!
    var selected: GameInstallation?
    let pathLabel = NSTextField(wrappingLabelWithString: "Choose your existing native macOS game installation.")
    let status = NSTextField(wrappingLabelWithString: "Requires BepInEx, ShopOS 1.1.4 and Enhanced Prefab Loader 6.2.16. Quit the game before installing.")
    let installButton = NSButton(title: "Install Plugins", target: nil, action: nil)
    let restoreButton = NSButton(title: "Restore Previous Files", target: nil, action: nil)
    lazy var engine = InstallerCore(payload: Bundle.main.resourceURL!.appendingPathComponent("Payload"), isRunning: { game in
        NSWorkspace.shared.runningApplications.contains { $0.bundleURL?.resolvingSymlinksInPath() == game.app.resolvingSymlinksInPath() }
    })
    func applicationDidFinishLaunching(_ notification: Notification) {
        window = NSWindow(contentRect: NSRect(x: 0,y: 0,width: 600,height: 530), styleMask: [.titled,.closable,.miniaturizable], backing: .buffered, defer: false)
        window.title = "TCG Plugin Installer"
        window.center()
        let menu = NSMenu(); let item = NSMenuItem(); menu.addItem(item)
        let appMenu = NSMenu(); appMenu.addItem(withTitle: "Quit TCG Plugin Installer", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"); item.submenu = appMenu; NSApp.mainMenu = menu
        let title = NSTextField(labelWithString: "Install TCG menu plugins")
        title.font = .systemFont(ofSize: 24, weight: .bold)
        let subtitle = NSTextField(wrappingLabelWithString: "Install our colorful shop menu, creator credits and clear loading tips into your compatible Mac game setup.")
        subtitle.textColor = .secondaryLabelColor
        let choose = NSButton(title: "Choose Game Folder…", target: self, action: #selector(chooseFolder))
        pathLabel.font = .systemFont(ofSize: 12); pathLabel.textColor = .secondaryLabelColor
        installButton.target = self; installButton.action = #selector(install); installButton.bezelStyle = .rounded; installButton.isEnabled = false
        restoreButton.target = self; restoreButton.action = #selector(restore); restoreButton.bezelStyle = .rounded; restoreButton.isEnabled = false
        let buttons = NSStackView(views: [installButton,restoreButton]); buttons.spacing = 10
        let project = NSButton(title: "Project & Credits ↗", target: self, action: #selector(openProject)); project.bezelStyle = .rounded
        let foot = NSTextField(wrappingLabelWithString: "Unofficial • v0.1.0 • Apple Silicon\nGame files and third-party mods are supplied by you. This app installs plugins into an existing native Mac player.")
        foot.font = .systemFont(ofSize: 11); foot.textColor = .secondaryLabelColor
        let stack = NSStackView(views: [title,subtitle,choose,pathLabel,buttons,status,project,foot]); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 16; stack.translatesAutoresizingMaskIntoConstraints = false
        window.contentView!.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: window.contentView!.leadingAnchor,constant:28),stack.trailingAnchor.constraint(equalTo: window.contentView!.trailingAnchor,constant:-28),stack.topAnchor.constraint(equalTo: window.contentView!.topAnchor,constant:28)])
        for label in [subtitle,pathLabel,status,foot] { label.widthAnchor.constraint(lessThanOrEqualToConstant:544).isActive = true }
        window.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true)
    }
    @objc func chooseFolder() {
        let panel = NSOpenPanel(); panel.canChooseDirectories = true; panel.canChooseFiles = true; panel.allowsMultipleSelection = false; panel.treatsFilePackagesAsDirectories = false; panel.prompt = "Select Installation"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { selected = try engine.validate(selection:url); pathLabel.stringValue = selected!.root.path; status.stringValue = "Compatible game structure found. Ready to install."; installButton.isEnabled = true; restoreButton.isEnabled = true }
        catch { selected = nil; installButton.isEnabled = false; restoreButton.isEnabled = false; status.stringValue = error.localizedDescription }
    }
    @objc func install() { guard let game = selected else { return }; do { status.stringValue = try engine.install(game) } catch { status.stringValue = error.localizedDescription } }
    @objc func restore() { guard let game = selected else { return }; do { status.stringValue = try engine.restore(game) } catch { status.stringValue = error.localizedDescription } }
    @objc func openProject() { NSWorkspace.shared.open(URL(string:"https://github.com/thanhphuchuynh/TCG")!) }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}
