using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A golf ball moving in S^3 under gravity.
///
/// State is a point <see cref="pos"/> on S^3 and a tangent velocity <see cref="vel"/> in m/s.
/// Between forces the ball follows a great circle exactly: each step is a rotation in the
/// plane (pos, vel). Gravity pulls towards the nearest ground along -<see cref="GravityUp"/>.
/// The ground contact keeps the centre exactly one radius above the ground; bounces, rolling friction and obstacle collisions are applied there.
/// The visual <see cref="frame"/> is carried by the same rotations, plus a rolling spin.
/// </summary>
public class S3Ball
{
    public Vector4 pos, vel;
    public Matrix4x4 frame = Matrix4x4.identity;
    public bool grounded;
    Vector4 lastUp = S3.EY;

    public float R = 12f;
    public float radius = 0.1f;
    public float gravity = 9.81f;
    public float restitution = 0.42f;
    public float bounceGrip = 0.8f;     // fraction of tangential speed kept through a bounce
    public float bounceSpeed = 0.9f;    // below this normal speed the ball rolls instead of bouncing
    public float airDrag = 0.06f;       // per second

    public float HeightM => Mathf.Asin(Mathf.Clamp(pos.y, -1f, 1f)) * R;   // centre height above ground
    public Vector4 Foot => S3.Foot(pos);
    public float Speed => vel.magnitude;

    public void Place(Vector4 groundPoint)
    {
        pos = S3.Lift(S3.Foot(groundPoint), radius / R);
        vel = Vector4.zero;
        frame = S3.FrameAt(pos, S3.UpAt(pos), S3.EZ);
        lastUp = S3.UpAt(pos);
        grounded = true;
    }

    /// <summary>
    /// Unit tangent pointing away from the nearest ground. Past the pole of the hemisphere
    /// (a quarter lap straight up) the nearest ground is on the far side, so this flips by itself.
    /// Exactly at the pole every direction is "down": the ball keeps falling the way it is heading,
    /// or, if it is at rest there, the way gravity last pointed.
    /// </summary>
    public Vector4 GravityUp()
    {
        Vector4 u = S3.EY - pos.y * pos;
        float m = u.magnitude;
        if (m > 1e-5f) return lastUp = u / m;
        float s = vel.magnitude;
        if (s > 1e-4f) return lastUp = -vel / s;
        Vector4 t = S3.Ortho(lastUp, pos);
        if (t == Vector4.zero) t = S3.Ortho(S3.EZ, pos);   // no usable history: pick a side
        if (t == Vector4.zero) t = S3.Ortho(S3.EX, pos);
        return lastUp = t;
    }

    public void Step(float dt, float rollFriction, List<S3World.Obstacle> obstacles)
    {
        vel -= gravity * dt * GravityUp();
        vel *= Mathf.Max(0, 1 - airDrag * dt);
        vel = S3.Tangent(pos, vel);

        // free flight along the great circle
        float speed = vel.magnitude;
        if (speed > 1e-7f)
        {
            var rot = S3.PlaneRotation(pos, vel / speed, speed * dt / R);
            pos = rot * pos;
            vel = rot * vel;
            frame = rot * frame;
            lastUp = rot * lastUp;   // parallel transport along the step
        }

        // ground contact
        grounded = false;
        if (HeightM <= radius)
        {
            pos = S3.Lift(Foot, radius / R);
            Vector4 n = S3.UpAt(pos);
            vel = S3.Tangent(pos, vel);
            float vn = Vector4.Dot(vel, n);
            if (vn < -bounceSpeed)
            {
                vel -= (1 + restitution) * vn * n;
                Vector4 vt = vel - Vector4.Dot(vel, n) * n;
                vel -= (1 - bounceGrip) * vt;
            }
            else
            {
                vel -= vn * n;
                grounded = true;
                float s = vel.magnitude;
                if (s > 1e-6f)
                {
                    Roll(n, vel / s, s * dt);
                    vel *= Mathf.Max(0, s - rollFriction * dt) / s;
                }
            }
        }

        // trunks, rocks and houses as vertical cylinders
        float reach = Mathf.Cos(Mathf.Min(12f / R, 3f));   // nothing is wider than ~12 m
        if (obstacles != null)
            foreach (var o in obstacles)
            {
                // cheap reject first: far-away obstacles fail a single dot product
                if (Vector4.Dot(pos, o.foot) < reach) continue;
                float h = HeightM;
                if (h - radius > o.height) continue;
                Vector4 foot = Foot;
                float d = S3.Dist(foot, o.foot) * R, min = o.radius + radius;
                if (d >= min || d < 1e-5f) continue;
                Vector4 away = -S3.DirTo(foot, o.foot);
                pos = S3.Lift(S3.Walk(foot, away, (min - d) / R), h / R);
                Vector4 a = S3.Ortho(away, pos);
                float va = Vector4.Dot(vel, a);
                if (va < 0) vel -= 1.55f * va * a;
                vel = S3.Tangent(pos, vel);
            }

        // keep the frame orthonormal and attached to the ball
        Vector4 c0 = S3.Ortho(frame.GetColumn(0), pos);
        Vector4 c1 = S3.Ortho(frame.GetColumn(1), pos, c0);
        Vector4 c2 = S3.Ortho(frame.GetColumn(2), pos, c0, c1);
        frame = new Matrix4x4(c0, c1, c2, pos);
    }

    /// <summary>Spin the ball so its top turns towards the direction of travel.</summary>
    void Roll(Vector4 up, Vector4 dir, float distance)
    {
        Matrix4x4 inv = frame.transpose;
        Vector4 upL = inv * up, dirL = inv * dir;
        upL.w = 0; dirL.w = 0;
        upL.Normalize();
        dirL = S3.Ortho(dirL, upL);
        if (dirL == Vector4.zero) return;
        frame *= S3.PlaneRotation(upL, dirL, distance / radius);
    }

    /// <summary>Launch at the given speed along horizontal direction dir with loft (radians).</summary>
    public void Launch(Vector4 dir, float speed, float loft)
    {
        Vector4 up = S3.UpAt(pos);
        Vector4 d = S3.Ortho(dir, pos, up);
        vel = speed * (Mathf.Cos(loft) * d + Mathf.Sin(loft) * up);
        grounded = false;
    }
}
