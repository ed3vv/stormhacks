import Foundation
import simd

final class WorldModel {
    private(set) var position = SIMD3<Float>(1, 0, 0)
    private(set) var forward = SIMD3<Float>(0, 1, 0)
    private(set) var right = SIMD3<Float>(0, 0, 1)
    private(set) var pitch: Float = -0.20
    private(set) var distanceFromStart: Float = 0

    private(set) var ballPosition = SIMD3<Float>(1, 0, 0)
    private(set) var aimDirection = SIMD3<Float>(0, 1, 0)
    private(set) var shotPower: Float = 0
    private(set) var strokes = 0
    private(set) var isCharging = false
    private(set) var isComplete = false
    private(set) var isAirborne = false
    private(set) var isAimLocked = false

    private var groundVelocity = SIMD3<Float>(repeating: 0)
    private var airCenter = SIMD4<Float>(1, 0, 0, 0)
    private var airVelocity = SIMD4<Float>(repeating: 0)
    private var supportObstacle: Int?
    private var supportAltitude: Float = 0
    private var baseAimDirection = SIMD3<Float>(0, 1, 0)
    private var lockedCameraDirection = SIMD3<Float>(0, 1, 0)
    private var aimPhase: Float = 0
    private var powerPhase: Float = 0

    let ballRadius: Float = 0.026
    let holeRadius: Float = 0.052
    let holePosition = SIMD3<Float>(cos(1.65), sin(1.65), 0)

    var ballRenderCenter: SIMD4<Float> {
        if isAirborne { return airCenter }
        return restingCenter(at: ballPosition, altitude: supportAltitude)
    }

    var cameraSupportAltitude: Float { isAirborne ? 0 : supportAltitude }

    var isBallMoving: Bool {
        isAirborne || simd_length_squared(groundVelocity) > 0.000001
    }

    var statusText: String {
        if isComplete { return "HOLED! Press R to play again" }
        if isAirborne { return "Ball airborne…" }
        if isBallMoving { return "Ball rolling…" }
        if isCharging { return "Power moving — press Space to shoot" }
        return "Aim moving — press Space to lock direction"
    }

    private struct Obstacle {
        let anchor: SIMD3<Float>
        let axisX: SIMD3<Float>
        let axisY: SIMD3<Float>
        let halfSize: SIMD3<Float>
    }

    private let obstacles: [Obstacle] = [
        Obstacle(anchor: SIMD3(-0.1582667,-0.5514225,-0.8190756), axisX: SIMD3(0.9611929,-0.2758772,0), axisY: SIMD3(-0.2259643,-0.7872897,0.5736855), halfSize: SIMD3(0.05729,0.0441781,0.0591369)),
        Obstacle(anchor: SIMD3(-0.4729068,-0.5030331,-0.7234064), axisX: SIMD3(0.7285874,-0.6849528,0), axisY: SIMD3(-0.4954992,-0.5270648,0.6904225), halfSize: SIMD3(0.0279613,0.0362004,0.1319101)),
        Obstacle(anchor: SIMD3(0.9806302,-0.1956148,-0.0099635), axisX: SIMD3(0.1956245,0.9806789,0), axisY: SIMD3(0.009771,-0.0019491,0.9999504), halfSize: SIMD3(0.0258073,0.0334518,0.0521049)),
        Obstacle(anchor: SIMD3(-0.9222494,-0.2086856,-0.3254327), axisX: SIMD3(0.2206993,-0.9753419,0), axisY: SIMD3(-0.3174081,-0.0718228,0.9455652), halfSize: SIMD3(0.0912092,0.0392725,0.0722301)),
        Obstacle(anchor: SIMD3(-0.338032,-0.5352655,-0.7740964), axisX: SIMD3(0.8455106,-0.5339586,0), axisY: SIMD3(-0.4133354,-0.6545067,0.6330678), halfSize: SIMD3(0.0511273,0.0691915,0.0632492)),
        Obstacle(anchor: SIMD3(-0.3316531,0.9352964,0.1233968), axisX: SIMD3(-0.9424996,-0.3342073,0), axisY: SIMD3(0.0412401,-0.1163014,0.9923574), halfSize: SIMD3(0.0332443,0.0283977,0.0885131)),
        Obstacle(anchor: SIMD3(0.7786598,-0.625421,-0.0503737), axisX: SIMD3(0.626216,0.7796496,0), axisY: SIMD3(0.0392738,-0.0315448,0.9987304), halfSize: SIMD3(0.0835847,0.0368342,0.0447193)),
        Obstacle(anchor: SIMD3(-0.0077993,-0.9517934,-0.3066407), axisX: SIMD3(0.9999664,-0.008194,0), axisY: SIMD3(-0.0025126,-0.3066304,0.9518253), halfSize: SIMD3(0.03638,0.0305436,0.0934684)),
        Obstacle(anchor: SIMD3(0.5966121,0.5668338,0.568114), axisX: SIMD3(-0.6887829,0.7249677,0), axisY: SIMD3(-0.4118643,-0.3913072,0.8229499), halfSize: SIMD3(0.0573414,0.069398,0.2121751)),
        Obstacle(anchor: SIMD3(0.8240269,-0.5635792,-0.0579502), axisX: SIMD3(0.5645279,0.825414,0), axisY: SIMD3(0.0478329,-0.0327145,0.9983195), halfSize: SIMD3(0.0447087,0.0623424,0.1461799)),
        Obstacle(anchor: SIMD3(0.2587637,-0.9428267,0.2100455), axisX: SIMD3(0.9643396,0.2646681,0), axisY: SIMD3(-0.0555923,0.2025552,0.9776916), halfSize: SIMD3(0.044286,0.0401495,0.0874475)),
        Obstacle(anchor: SIMD3(0.7525857,0.577199,-0.316948), axisX: SIMD3(-0.6085753,0.7934961,0), axisY: SIMD3(0.251497,0.1928868,0.9484429), halfSize: SIMD3(0.0386598,0.0344847,0.1369906)),
        Obstacle(anchor: SIMD3(0.8869949,0.4617792,0), axisX: SIMD3(-0.4617792,0.8869949,0), axisY: SIMD3(0,0,1), halfSize: SIMD3(0.18,0.14,0.018))
    ]

    init() {
        airCenter = restingCenter(at: ballPosition, altitude: 0)
        updateCamera()
    }

    func reset() {
        ballPosition = SIMD3<Float>(1, 0, 0)
        aimDirection = SIMD3<Float>(0, 1, 0)
        groundVelocity = .zero
        airCenter = restingCenter(at: ballPosition, altitude: 0)
        airVelocity = .zero
        shotPower = 0
        strokes = 0
        isCharging = false
        isComplete = false
        isAirborne = false
        isAimLocked = false
        supportObstacle = nil
        supportAltitude = 0
        baseAimDirection = SIMD3<Float>(0, 1, 0)
        lockedCameraDirection = SIMD3<Float>(0, 1, 0)
        aimPhase = 0
        powerPhase = 0
        pitch = -0.20
        updateCamera()
    }

    func pressAction() {
        guard !isBallMoving, !isComplete else { return }
        if !isAimLocked {
            isAimLocked = true
            isCharging = true
            powerPhase = 0
            shotPower = 0
            return
        }
        guard isCharging else { return }
        isCharging = false
        let speed: Float = 0.42 + shotPower * 1.18
        let launchAngle: Float = 24 * .pi / 180
        airCenter = restingCenter(at: ballPosition, altitude: supportAltitude)
        let horizontal = SIMD4<Float>(aimDirection.x, aimDirection.y,
                                      aimDirection.z, 0)
        let centerAltitude = supportAltitude + ballRadius
        let upward = SIMD4<Float>(-ballPosition.x * sin(centerAltitude),
                                  -ballPosition.y * sin(centerAltitude),
                                  -ballPosition.z * sin(centerAltitude),
                                  cos(centerAltitude))
        airVelocity = (horizontal * cos(launchAngle)
                     + upward * sin(launchAngle)) * speed
        groundVelocity = .zero
        lockedCameraDirection = aimDirection
        isAirborne = true
        supportObstacle = nil
        supportAltitude = 0
        strokes += 1
    }

    func look(deltaX: Float, deltaY: Float) {
        pitch = max(-0.85, min(0.42, pitch - deltaY * 0.0025))
        updateCamera()
    }

    func update(deltaTime: Float) {
        let wasMoving = isBallMoving
        if !isBallMoving && !isComplete {
            if !isAimLocked {
                aimPhase += deltaTime
                aimDirection = rotated(baseAimDirection,
                                       around: ballPosition,
                                       by: sin(aimPhase * 1.7) * 0.72)
            } else if isCharging {
                powerPhase += deltaTime
                shotPower = 0.5 - 0.5 * cos(powerPhase * 3.2)
            }
        }

        if isAirborne {
            updateAirborne(deltaTime: deltaTime)
        } else {
            updateRolling(deltaTime: deltaTime)
        }
        if wasMoving && !isBallMoving && !isComplete { prepareNextShot() }
        distanceFromStart = acos(max(-1, min(1, ballPosition.x)))
        updateCamera()
    }

    private func rotated(_ direction: SIMD3<Float>, around point: SIMD3<Float>,
                         by angle: Float) -> SIMD3<Float> {
        let side = simd_normalize(simd_cross(point, direction))
        return simd_normalize(direction * cos(angle) + side * sin(angle))
    }

    private func prepareNextShot() {
        baseAimDirection = aimDirection
        lockedCameraDirection = aimDirection
        isAimLocked = false
        isCharging = false
        aimPhase = 0
        powerPhase = 0
        shotPower = 0
    }

    private func updateAirborne(deltaTime: Float) {
        var timeRemaining = deltaTime
        while timeRemaining > 0 && isAirborne {
            let step = min(timeRemaining, 1 / 180)
            timeRemaining -= step

            let groundPoint = simd_normalize(airCenter.xyz)
            let closestGround = SIMD4<Float>(groundPoint.x, groundPoint.y,
                                             groundPoint.z, 0)
            let gravityDirection = simd_normalize(closestGround
                - airCenter * simd_dot(airCenter, closestGround))
            airVelocity += gravityDirection * (1.32 * step)
            airVelocity -= airCenter * simd_dot(airVelocity, airCenter)
            airVelocity *= exp(-0.035 * step)

            let speed = simd_length(airVelocity)
            let previousCenter = airCenter
            if speed > 0.0001 {
                let direction = airVelocity / speed
                let travel = speed * step
                airCenter = simd_normalize(previousCenter * cos(travel)
                                         + direction * sin(travel))
                let transported = simd_normalize(-previousCenter * sin(travel)
                                                + direction * cos(travel))
                airVelocity = transported * speed
            }

            if handleAirCollision(from: previousCenter) {
                if !isAirborne { break }
                continue
            }

            ballPosition = simd_normalize(airCenter.xyz)
            let horizontalVelocity = airVelocity.xyz
                - simd_dot(airVelocity.xyz, ballPosition) * ballPosition
            if simd_length_squared(horizontalVelocity) > 0.000001 {
                aimDirection = simd_normalize(horizontalVelocity)
            }

            let altitude = atan2(abs(airCenter.w), simd_length(airCenter.xyz))
            let side: Float = airCenter.w >= 0 ? 1 : -1
            let outward = SIMD4<Float>(-ballPosition.x * sin(altitude),
                                       -ballPosition.y * sin(altitude),
                                       -ballPosition.z * sin(altitude),
                                       side * cos(altitude))
            let outwardSpeed = simd_dot(airVelocity, outward)
            if altitude <= ballRadius && outwardSpeed <= 0 {
                if -outwardSpeed > 0.14 {
                    airCenter = restingCenter(at: ballPosition, altitude: 0.001)
                    reflectAirVelocity(normal: outward, restitution: 0.38)
                } else {
                    landBall()
                }
            }
        }
    }

    private func landBall() {
        ballPosition = simd_normalize(airCenter.xyz)
        var horizontal = airVelocity.xyz
        horizontal -= simd_dot(horizontal, ballPosition) * ballPosition
        groundVelocity = horizontal / max(cos(ballRadius), 0.001)
        if simd_length_squared(groundVelocity) > 0.000001 {
            aimDirection = simd_normalize(groundVelocity)
        }
        supportObstacle = nil
        supportAltitude = 0
        airCenter = restingCenter(at: ballPosition, altitude: 0)
        airVelocity = .zero
        isAirborne = false
        checkHole()
    }

    private func handleAirCollision(from previousCenter: SIMD4<Float>) -> Bool {
        let margin = tan(ballRadius)
        for (index, obstacle) in obstacles.enumerated() {
            guard let previous = localCoordinates(previousCenter, obstacle),
                  let current = localCoordinates(airCenter, obstacle) else { continue }
            let top = obstacle.halfSize.z * 2
            let insideX = abs(current.x) <= obstacle.halfSize.x + margin
            let insideY = abs(current.y) <= obstacle.halfSize.y + margin
            guard insideX && insideY else { continue }

            let landingHeight = top + margin
            if previous.z >= landingHeight && current.z <= landingHeight {
                ballPosition = simd_normalize(airCenter.xyz)
                let groundDenominator = max(simd_dot(ballPosition, obstacle.anchor), 0.02)
                let topAltitude = atan(top * groundDenominator)
                let topNormal = boxFaceNormal(obstacle: obstacle,
                                              dimension: 2,
                                              side: 1,
                                              boundary: landingHeight,
                                              at: airCenter)
                let impactSpeed = max(0, -simd_dot(airVelocity, topNormal))
                if impactSpeed > 0.14 {
                    airCenter = restingCenter(at: ballPosition,
                                              altitude: topAltitude + 0.001)
                    reflectAirVelocity(normal: topNormal, restitution: 0.42)
                } else {
                    supportAltitude = topAltitude
                    supportObstacle = index
                    var horizontal = airVelocity.xyz
                    horizontal -= simd_dot(horizontal, ballPosition) * ballPosition
                    groundVelocity = horizontal / max(cos(supportAltitude + ballRadius), 0.001)
                    if simd_length_squared(groundVelocity) > 0.000001 {
                        aimDirection = simd_normalize(groundVelocity)
                    }
                    airCenter = restingCenter(at: ballPosition, altitude: supportAltitude)
                    airVelocity = .zero
                    isAirborne = false
                }
                return true
            }

            if current.z >= -margin && current.z <= top + margin {
                airCenter = previousCenter
                let expandedX = obstacle.halfSize.x + margin
                let expandedY = obstacle.halfSize.y + margin
                let distances = SIMD4<Float>(expandedX - abs(current.x),
                                             expandedY - abs(current.y),
                                             current.z + margin,
                                             top + margin - current.z)
                var dimension = 0
                var faceSide: Float = current.x >= 0 ? 1 : -1
                var boundary = expandedX * faceSide
                var smallest = distances.x
                if distances.y < smallest {
                    dimension = 1
                    faceSide = current.y >= 0 ? 1 : -1
                    boundary = expandedY * faceSide
                    smallest = distances.y
                }
                if distances.z < smallest {
                    dimension = 2
                    faceSide = -1
                    boundary = -margin
                    smallest = distances.z
                }
                if distances.w < smallest {
                    dimension = 2
                    faceSide = 1
                    boundary = top + margin
                }
                let normal = boxFaceNormal(obstacle: obstacle,
                                           dimension: dimension,
                                           side: faceSide,
                                           boundary: boundary,
                                           at: airCenter)
                reflectAirVelocity(normal: normal, restitution: 0.58)
                return true
            }
        }
        return false
    }

    private func updateRolling(deltaTime: Float) {
        var speed = simd_length(groundVelocity)
        guard speed > 0.001 else {
            groundVelocity = .zero
            return
        }
        let travel = speed * deltaTime
        let direction = groundVelocity / speed
        let nextPosition = simd_normalize(ballPosition * cos(travel)
                                         + direction * sin(travel))
        let transported = simd_normalize(-ballPosition * sin(travel)
                                        + direction * cos(travel))

        let margin = tan(ballRadius)
        if let supportIndex = supportObstacle {
            let support = obstacles[supportIndex]
            let denominator = simd_dot(nextPosition, support.anchor)
            let x = simd_dot(nextPosition, support.axisX) / max(denominator, 0.02)
            let y = simd_dot(nextPosition, support.axisY) / max(denominator, 0.02)
            if denominator <= 0.02
                || abs(x) > support.halfSize.x - margin
                || abs(y) > support.halfSize.y - margin {
                ballPosition = nextPosition
                aimDirection = transported
                let altitude = supportAltitude + ballRadius
                airCenter = restingCenter(at: ballPosition, altitude: supportAltitude)
                airVelocity = SIMD4<Float>(transported.x * speed * cos(altitude),
                                           transported.y * speed * cos(altitude),
                                           transported.z * speed * cos(altitude), 0)
                isAirborne = true
                supportObstacle = nil
                supportAltitude = 0
                return
            }
            supportAltitude = atan((support.halfSize.z * 2) * denominator)
        }

        for (index, obstacle) in obstacles.enumerated() where index != supportObstacle {
            let denominator = simd_dot(nextPosition, obstacle.anchor)
            if denominator <= 0.02 { continue }
            let x = simd_dot(nextPosition, obstacle.axisX) / denominator
            let y = simd_dot(nextPosition, obstacle.axisY) / denominator
            if abs(x) <= obstacle.halfSize.x + margin
                && abs(y) <= obstacle.halfSize.y + margin {
                let expandedX = obstacle.halfSize.x + margin
                let expandedY = obstacle.halfSize.y + margin
                let useX = abs(x) / expandedX > abs(y) / expandedY
                let sign: Float = useX ? (x >= 0 ? 1 : -1)
                                       : (y >= 0 ? 1 : -1)
                let rawNormal = useX
                    ? sign * obstacle.axisX - expandedX * obstacle.anchor
                    : sign * obstacle.axisY - expandedY * obstacle.anchor
                let normal = simd_normalize(rawNormal
                    - simd_dot(rawNormal, ballPosition) * ballPosition)
                let normalSpeed = simd_dot(groundVelocity, normal)
                if normalSpeed < 0 {
                    let tangentVelocity = groundVelocity - normal * normalSpeed
                    groundVelocity = tangentVelocity * 0.92
                                   - normal * normalSpeed * 0.68
                    aimDirection = simd_normalize(groundVelocity)
                }
                shotPower = 0
                return
            }
        }

        ballPosition = nextPosition
        aimDirection = transported
        if supportObstacle == nil { checkHole() }
        if isComplete { return }

        speed = max(0, speed - 0.54 * deltaTime)
        groundVelocity = speed > 0.001 ? transported * speed : .zero
        if speed <= 0.001 { shotPower = 0 }
    }

    private func checkHole() {
        if simd_dot(ballPosition, holePosition) > cos(holeRadius) {
            ballPosition = holePosition
            groundVelocity = .zero
            airVelocity = .zero
            isAirborne = false
            isComplete = true
            shotPower = 0
        }
    }

    private func boxFaceNormal(obstacle: Obstacle, dimension: Int,
                               side: Float, boundary: Float,
                               at center: SIMD4<Float>) -> SIMD4<Float> {
        let raw: SIMD4<Float>
        if dimension == 0 {
            let spatial = side * obstacle.axisX
                - abs(boundary) * obstacle.anchor
            raw = SIMD4(spatial.x, spatial.y, spatial.z, 0)
        } else if dimension == 1 {
            let spatial = side * obstacle.axisY
                - abs(boundary) * obstacle.anchor
            raw = SIMD4(spatial.x, spatial.y, spatial.z, 0)
        } else {
            let spatial = -abs(boundary) * obstacle.anchor
            raw = SIMD4(spatial.x, spatial.y, spatial.z, side)
        }
        return simd_normalize(raw - center * simd_dot(raw, center))
    }

    private func reflectAirVelocity(normal: SIMD4<Float>, restitution: Float) {
        let normalSpeed = simd_dot(airVelocity, normal)
        guard normalSpeed < 0 else { return }
        let tangentVelocity = airVelocity - normal * normalSpeed
        airVelocity = tangentVelocity * 0.90
                    - normal * normalSpeed * restitution
        airVelocity -= airCenter * simd_dot(airVelocity, airCenter)
    }

    private func localCoordinates(_ center: SIMD4<Float>,
                                  _ obstacle: Obstacle) -> SIMD3<Float>? {
        let denominator = simd_dot(center.xyz, obstacle.anchor)
        guard denominator > 0.02 else { return nil }
        return SIMD3(simd_dot(center.xyz, obstacle.axisX) / denominator,
                     simd_dot(center.xyz, obstacle.axisY) / denominator,
                     center.w / denominator)
    }

    private func restingCenter(at groundPoint: SIMD3<Float>,
                               altitude: Float) -> SIMD4<Float> {
        let centerAltitude = altitude + ballRadius
        return SIMD4<Float>(groundPoint.x * cos(centerAltitude),
                            groundPoint.y * cos(centerAltitude),
                            groundPoint.z * cos(centerAltitude),
                            sin(centerAltitude))
    }

    private func updateCamera() {
        let followDistance: Float = 0.30
        var cameraDirection = isBallMoving ? lockedCameraDirection
                                           : baseAimDirection
        cameraDirection -= simd_dot(cameraDirection, ballPosition) * ballPosition
        if simd_length_squared(cameraDirection) < 0.000001 {
            cameraDirection = aimDirection
        } else {
            cameraDirection = simd_normalize(cameraDirection)
        }
        position = simd_normalize(ballPosition * cos(followDistance)
                                 - cameraDirection * sin(followDistance))
        forward = simd_normalize(ballPosition * sin(followDistance)
                                + cameraDirection * cos(followDistance))
        right = simd_normalize(simd_cross(position, forward))
    }
}

private extension SIMD4 where Scalar == Float {
    var xyz: SIMD3<Float> { SIMD3(x, y, z) }
}
