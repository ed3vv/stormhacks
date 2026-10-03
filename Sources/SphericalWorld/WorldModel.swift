import Foundation
import simd

final class WorldModel {
    private(set) var position = SIMD3<Float>(1, 0, 0)
    private(set) var forward = SIMD3<Float>(0, 1, 0)
    private(set) var right = SIMD3<Float>(0, 0, 1)
    private(set) var pitch: Float = 0
    private(set) var distanceFromStart: Float = 0
    let ballDirection = simd_normalize(SIMD3<Float>(0.8, 0.6, 0))
    let ballRadius: Float = 0.12
    private var correctionCounter = 0

    func reset() {
        position = SIMD3<Float>(1, 0, 0)
        forward = SIMD3<Float>(0, 1, 0)
        right = SIMD3<Float>(0, 0, 1)
        pitch = 0
        distanceFromStart = 0
    }

    func look(deltaX: Float, deltaY: Float) {
        let angle = deltaX * 0.0025
        let oldForward = forward
        forward = oldForward * cos(angle) + right * sin(angle)
        right = oldForward * -sin(angle) + right * cos(angle)
        pitch = max(-1.56, min(1.56, pitch - deltaY * 0.0025))
    }

    func update(forwardAmount: Float, strafeAmount: Float, deltaTime: Float, running: Bool) {
        let speed: Float = running ? 2.2 : 0.7
        if forwardAmount != 0 { walk(tangent: forward, amount: forwardAmount * speed * deltaTime, isForward: true) }
        if strafeAmount != 0 { walk(tangent: right, amount: strafeAmount * speed * deltaTime, isForward: false) }
        correctionCounter += 1
        if correctionCounter == 30 { correctBasis(); correctionCounter = 0 }
        distanceFromStart = acos(max(-1, min(1, position.x)))
    }

    private func walk(tangent: SIMD3<Float>, amount: Float, isForward: Bool) {
        let nextPosition = position * cos(amount) + tangent * sin(amount)
        if simd_dot(nextPosition, ballDirection) > cos(ballRadius + 0.025) { return }
        let nextTangent = -position * sin(amount) + tangent * cos(amount)
        position = nextPosition
        if isForward { forward = nextTangent } else { right = nextTangent }
    }

    private func correctBasis() {
        position = simd_normalize(position)
        forward = simd_normalize(forward - simd_dot(forward, position) * position)
        right = simd_normalize(right - simd_dot(right, position) * position
            - simd_dot(right, forward) * forward)
    }
}
