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
    var golfBall = SIMD4<Float>(0, 0, 0, 0)
    var hole = SIMD4<Float>(0, 0, 0, 0)
    var aim = SIMD4<Float>(0, 0, 0, 0)
    var game = SIMD4<Float>(0, 0, 0, 0)
}

final class WorldRenderer: NSObject, MTKViewDelegate {
    let model: WorldModel
    var statsHandler: ((Int) -> Void)?
    private let commandQueue: MTLCommandQueue
    private let pipeline: MTLRenderPipelineState
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
        model.update(deltaTime: deltaTime)
        guard let pass = view.currentRenderPassDescriptor,
              let drawable = view.currentDrawable,
              let buffer = commandQueue.makeCommandBuffer(),
              let encoder = buffer.makeRenderCommandEncoder(descriptor: pass) else { return }

        // Visual eye height in the fourth-dimensional direction. Raising this
        // opens the floor-to-far-side volume without changing the world radius.
        let height: Float = 0.16 + model.cameraSupportAltitude
        let p = model.position
        let f = model.forward
        let r = model.right
        let player = SIMD4<Float>(p.x * cos(height), p.y * cos(height), p.z * cos(height), sin(height))
        let groundForward = SIMD4<Float>(f.x, f.y, f.z, 0)
        let groundUp = SIMD4<Float>(-p.x * sin(height), -p.y * sin(height), -p.z * sin(height), cos(height))
        let viewForward = groundForward * cos(model.pitch) + groundUp * sin(model.pitch)
        let viewUp = -groundForward * sin(model.pitch) + groundUp * cos(model.pitch)
        var uniforms = Uniforms(
            resolution: SIMD2(Float(view.drawableSize.width), Float(view.drawableSize.height)),
            padding: .zero,
            player: player,
            forward: viewForward,
            right: SIMD4<Float>(r.x, r.y, r.z, 0),
            up: viewUp,
            golfBall: model.ballRenderCenter,
            hole: SIMD4<Float>(model.holePosition.x, model.holePosition.y,
                               model.holePosition.z, model.holeRadius),
            aim: SIMD4<Float>(model.aimDirection.x, model.aimDirection.y,
                              model.aimDirection.z, model.shotPower),
            game: SIMD4<Float>(model.isBallMoving ? 1 : 0,
                               model.isComplete ? 1 : 0,
                               model.ballRadius,
                               model.isAirborne ? 1 : 0)
        )
        encoder.setRenderPipelineState(pipeline)
        encoder.setFragmentBytes(&uniforms, length: MemoryLayout<Uniforms>.stride, index: 0)
        encoder.drawPrimitives(type: .triangle, vertexStart: 0, vertexCount: 3)
        encoder.endEncoding()
        buffer.present(drawable)
        buffer.commit()

        frameCount += 1
        if now - statsStart >= 0.25 {
            statsHandler?(Int((Double(frameCount) / (now - statsStart)).rounded()))
            statsStart = now
            frameCount = 0
        }
    }

}
