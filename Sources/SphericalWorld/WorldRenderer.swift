import AppKit
import MetalKit
import QuartzCore
import simd

private struct Uniforms {
    var resolution = SIMD2<Float>(0, 0)
    var padding = SIMD2<Float>(0, 0)
    var player = SIMD4<Float>(0, 0, 0, 0)
    var forward = SIMD4<Float>(0, 0, 0, 0)
    var right = SIMD4<Float>(0, 0, 0, 0)
    var up = SIMD4<Float>(0, 0, 0, 0)
    var ball = SIMD4<Float>(0, 0, 0, 0)
}

final class WorldRenderer: NSObject, MTKViewDelegate {
    let model: WorldModel
    var inputProvider: (() -> (forward: Float, strafe: Float, running: Bool))?
    var statsHandler: ((Int, Float) -> Void)?
    private let commandQueue: MTLCommandQueue
    private let pipeline: MTLRenderPipelineState
    private let letteringTexture: MTLTexture
    private let grassTexture: MTLTexture
    private var lastTime = CACurrentMediaTime()
    private var statsStart = CACurrentMediaTime()
    private var frameCount = 0

    init(view: MTKView, model: WorldModel) throws {
        self.model = model
        guard let device = view.device, let queue = device.makeCommandQueue() else {
            throw NSError(domain: "SphericalWorld", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "Metal is unavailable."])
        }
        commandQueue = queue
        letteringTexture = try Self.makeLetteringTexture(device: device)
        grassTexture = try Self.makeGrassTexture(device: device)
        let shaderURL = Bundle.module.url(forResource: "World", withExtension: "metal")!
        let source = try String(contentsOf: shaderURL, encoding: .utf8)
        let library = try device.makeLibrary(source: source, options: nil)
        let descriptor = MTLRenderPipelineDescriptor()
        descriptor.vertexFunction = library.makeFunction(name: "fullscreenVertex")
        descriptor.fragmentFunction = library.makeFunction(name: "worldFragment")
        descriptor.colorAttachments[0].pixelFormat = view.colorPixelFormat
        pipeline = try device.makeRenderPipelineState(descriptor: descriptor)
        super.init()
    }

    func mtkView(_ view: MTKView, drawableSizeWillChange size: CGSize) {}

    func draw(in view: MTKView) {
        let now = CACurrentMediaTime()
        let deltaTime = Float(min(now - lastTime, 0.05))
        lastTime = now
        let input = inputProvider?() ?? (forward: 0, strafe: 0, running: false)
        model.update(forwardAmount: input.forward, strafeAmount: input.strafe,
                     deltaTime: deltaTime, running: input.running)
        guard let pass = view.currentRenderPassDescriptor,
              let drawable = view.currentDrawable,
              let buffer = commandQueue.makeCommandBuffer(),
              let encoder = buffer.makeRenderCommandEncoder(descriptor: pass) else { return }

        let height: Float = 0.07
        let p = model.position
        let f = model.forward
        let r = model.right
        let player = SIMD4<Float>(p.x * cos(height), p.y * cos(height), p.z * cos(height), sin(height))
        let groundForward = SIMD4<Float>(f.x, f.y, f.z, 0)
        let groundUp = SIMD4<Float>(-p.x * sin(height), -p.y * sin(height), -p.z * sin(height), cos(height))
        let viewForward = groundForward * cos(model.pitch) + groundUp * sin(model.pitch)
        let viewUp = -groundForward * sin(model.pitch) + groundUp * cos(model.pitch)
        let ball = model.ballDirection
        let ballCos = cos(model.ballRadius)
        var uniforms = Uniforms(
            resolution: SIMD2(Float(view.drawableSize.width), Float(view.drawableSize.height)),
            padding: .zero,
            player: player,
            forward: viewForward,
            right: SIMD4<Float>(r.x, r.y, r.z, 0),
            up: viewUp,
            ball: SIMD4<Float>(ball.x * ballCos, ball.y * ballCos, ball.z * ballCos, sin(model.ballRadius))
        )
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 0)
        encoder.setFragmentTexture(letteringTexture, index: 0)
        encoder.setFragmentTexture(grassTexture, index: 1)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
        buffer.present(drawable)
        buffer.commit()

        frameCount += 1
        if now - statsStart >= 0.5 {
            statsHandler?(Int((Double(frameCount) / (now - statsStart)).rounded()), model.distanceFromStart)
            statsStart = now
            frameCount = 0
        }
    }

    private static func makeLetteringTexture(device: MTLDevice) throws -> MTLTexture {
        let size = NSSize(width: 1024, height: 256)
        let image = NSImage(size: size)
        image.lockFocus()
        NSColor.clear.setFill()
        NSRect(origin: .zero, size: size).fill(using: .copy)

        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 104, weight: .black),
            .foregroundColor: NSColor.white,
            .strokeColor: NSColor(calibratedWhite: 0.12, alpha: 1),
            .strokeWidth: -7,
            .paragraphStyle: paragraph
        ]
        let textRect = NSRect(x: 112, y: 63, width: 800, height: 130)
        NSAttributedString(string: "HELLO", attributes: attributes).draw(in: textRect)
        image.unlockFocus()

        guard let data = image.tiffRepresentation else {
            throw NSError(domain: "SphericalWorld", code: 2,
                          userInfo: [NSLocalizedDescriptionKey: "Could not create lettering texture."])
        }
        return try MTKTextureLoader(device: device).newTexture(
            data: data,
            options: [.SRGB: false, .textureUsage: MTLTextureUsage.shaderRead.rawValue]
        )
    }

    private static func makeGrassTexture(device: MTLDevice) throws -> MTLTexture {
        let side: CGFloat = 512
        let image = NSImage(size: NSSize(width: side, height: side))
        image.lockFocus()
        NSColor(calibratedRed: 0.025, green: 0.13, blue: 0.035, alpha: 1).setFill()
        NSRect(x: 0, y: 0, width: side, height: side).fill()

        var seed: UInt64 = 0x47_52_41_53_53
        func random() -> CGFloat {
            seed = seed &* 6_364_136_223_846_793_005 &+ 1_442_695_040_888_963_407
            return CGFloat((seed >> 40) & 0xFFFFFF) / CGFloat(0xFFFFFF)
        }

        // Draw tapered leaves and repeat edge-crossing leaves so the image tiles.
        for _ in 0..<5_500 {
            let origin = NSPoint(x: random() * side, y: random() * side)
            let length = 7 + random() * 19
            let width = 0.8 + random() * 2.2
            let angle = random() * .pi * 2
            let direction = NSPoint(x: cos(angle), y: sin(angle))
            let perpendicular = NSPoint(x: -direction.y, y: direction.x)
            let shade = random()
            NSColor(calibratedRed: 0.035 + 0.11 * shade,
                    green: 0.24 + 0.34 * shade,
                    blue: 0.035 + 0.07 * shade,
                    alpha: 0.88).setFill()

            for offsetX in [-side, 0, side] {
                for offsetY in [-side, 0, side] {
                    let base = NSPoint(x: origin.x + offsetX, y: origin.y + offsetY)
                    let tip = NSPoint(x: base.x + direction.x * length,
                                      y: base.y + direction.y * length)
                    let left = NSPoint(x: base.x + perpendicular.x * width,
                                       y: base.y + perpendicular.y * width)
                    let right = NSPoint(x: base.x - perpendicular.x * width,
                                        y: base.y - perpendicular.y * width)
                    let path = NSBezierPath()
                    path.move(to: left)
                    path.curve(to: tip,
                               controlPoint1: NSPoint(x: base.x + direction.x * length * 0.55
                                                     + perpendicular.x * width * 0.8,
                                                      y: base.y + direction.y * length * 0.55
                                                     + perpendicular.y * width * 0.8),
                               controlPoint2: NSPoint(x: tip.x + perpendicular.x * width * 0.18,
                                                      y: tip.y + perpendicular.y * width * 0.18))
                    path.curve(to: right,
                               controlPoint1: NSPoint(x: tip.x - perpendicular.x * width * 0.18,
                                                      y: tip.y - perpendicular.y * width * 0.18),
                               controlPoint2: NSPoint(x: base.x + direction.x * length * 0.42
                                                     - perpendicular.x * width * 0.7,
                                                      y: base.y + direction.y * length * 0.42
                                                     - perpendicular.y * width * 0.7))
                    path.close()
                    path.fill()
                }
            }
        }
        image.unlockFocus()

        guard let data = image.tiffRepresentation else {
            throw NSError(domain: "SphericalWorld", code: 3,
                          userInfo: [NSLocalizedDescriptionKey: "Could not create grass texture."])
        }
        return try MTKTextureLoader(device: device).newTexture(
            data: data,
            options: [.SRGB: false, .textureUsage: MTLTextureUsage.shaderRead.rawValue]
        )
    }
}
