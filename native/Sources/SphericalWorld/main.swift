import AppKit
import MetalKit

final class WorldView: MTKView {
    var pressedKeys = Set<UInt16>()
    var shiftPressed = false
    var model: WorldModel?
    private var mouseCaptured = false
    override var acceptsFirstResponder: Bool { true }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { releaseMouse() }
        else if event.keyCode == 15 { model?.reset() }
        else if event.keyCode == 49 {
            if !event.isARepeat { model?.pressAction() }
        }
        else { pressedKeys.insert(event.keyCode) }
    }
    override func keyUp(with event: NSEvent) {
        pressedKeys.remove(event.keyCode)
    }
    override func flagsChanged(with event: NSEvent) {
        shiftPressed = event.modifierFlags.contains(.shift)
    }
    override func mouseDown(with event: NSEvent) {
        if !mouseCaptured {
            mouseCaptured = true
            NSCursor.hide()
            CGAssociateMouseAndMouseCursorPosition(boolean_t(0))
        }
    }
    override func mouseMoved(with event: NSEvent) { applyMouse(event) }
    override func mouseDragged(with event: NSEvent) { applyMouse(event) }
    private func applyMouse(_ event: NSEvent) {
        guard mouseCaptured else { return }
        model?.look(deltaX: Float(event.deltaX), deltaY: Float(event.deltaY))
    }
    private func releaseMouse() {
        guard mouseCaptured else { return }
        mouseCaptured = false
        CGAssociateMouseAndMouseCursorPosition(boolean_t(1))
        NSCursor.unhide()
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var window: NSWindow!
    private var renderer: WorldRenderer!

    func applicationDidFinishLaunching(_ notification: Notification) {
        guard let device = MTLCreateSystemDefaultDevice() else {
            presentErrorAndQuit("This Mac does not support Metal.")
            return
        }
        let view = WorldView(frame: NSRect(x: 0, y: 0, width: 1100, height: 700), device: device)
        view.colorPixelFormat = .bgra8Unorm
        view.clearColor = MTLClearColorMake(0, 0, 0, 1)
        view.preferredFramesPerSecond = NSScreen.main?.maximumFramesPerSecond ?? 60
        view.enableSetNeedsDisplay = false
        view.isPaused = false
        window = NSWindow(contentRect: view.frame,
                          styleMask: [.titled, .closable, .miniaturizable, .resizable],
                          backing: .buffered, defer: false)
        window.title = "Spherical Plane World"
        window.center()
        window.contentView = view
        window.acceptsMouseMovedEvents = true
        window.makeKeyAndOrderFront(nil)
        window.makeFirstResponder(view)

        let fpsLabel = makeLabel(size: 14, weight: .semibold)
        let golfLabel = makeLabel(size: 14, weight: .semibold)
        let helpLabel = makeLabel(size: 13, weight: .regular)
        helpLabel.stringValue = "Space locks direction · Space again locks power and shoots · R resets"
        helpLabel.maximumNumberOfLines = 2
        view.addSubview(fpsLabel)
        view.addSubview(golfLabel)
        view.addSubview(helpLabel)
        NSLayoutConstraint.activate([
            fpsLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 14),
            fpsLabel.topAnchor.constraint(equalTo: view.topAnchor, constant: 14),
            golfLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 14),
            golfLabel.topAnchor.constraint(equalTo: fpsLabel.bottomAnchor, constant: 8),
            helpLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 14),
            helpLabel.topAnchor.constraint(equalTo: golfLabel.bottomAnchor, constant: 8)
        ])

        let model = WorldModel()
        view.model = model
        do { renderer = try WorldRenderer(view: view, model: model) }
        catch { presentErrorAndQuit(error.localizedDescription); return }
        renderer.statsHandler = { fps in
            fpsLabel.stringValue = "FPS: \(fps)"
            let filled = Int((model.shotPower * 12).rounded())
            let meter = String(repeating: "█", count: filled)
                + String(repeating: "░", count: 12 - filled)
            golfLabel.stringValue = "STROKES: \(model.strokes)   POWER [\(meter)]   \(model.statusText)"
        }
        view.delegate = renderer
        NSApp.activate(ignoringOtherApps: true)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }

    private func makeLabel(size: CGFloat, weight: NSFont.Weight) -> NSTextField {
        let label = NSTextField(labelWithString: "FPS: --")
        label.translatesAutoresizingMaskIntoConstraints = false
        label.font = .monospacedSystemFont(ofSize: size, weight: weight)
        label.textColor = NSColor(calibratedRed: 0.91, green: 0.93, blue: 1, alpha: 1)
        label.drawsBackground = true
        label.backgroundColor = NSColor(calibratedRed: 0.04, green: 0.05, blue: 0.12, alpha: 0.72)
        return label
    }

    private func presentErrorAndQuit(_ message: String) {
        let alert = NSAlert()
        alert.messageText = "Spherical World could not start"
        alert.informativeText = message
        alert.runModal()
        NSApp.terminate(nil)
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.regular)
app.run()
