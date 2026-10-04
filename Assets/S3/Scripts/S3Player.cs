using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person walker on the ground great-sphere of S^3.
///
/// The player is an SO(4) frame <see cref="Ground"/> = (right, up, forward, foot). Since the
/// ground is the great sphere y = 0, "up" is always e_y. Walking a distance s is a rotation in
/// the plane (foot, direction), applied in the local frame, which parallel-transports heading
/// for free. The eye is the foot lifted along up, then pitched. The Unity camera itself never moves:
/// it only supplies the projection, and the view comes from <see cref="CameraFrame"/>.
/// </summary>
[RequireComponent(typeof(Camera))]
public class S3Player : MonoBehaviour
{
    public float eyeHeight = 1.7f;
    public float walkSpeed = 4.5f;
    public float runMultiplier = 2.2f;
    public float lookSensitivity = 0.0022f;   // radians per pixel
    public float jumpSpeed = 4.5f;
    public float gravity = 12f;
    public float bodyRadius = 0.35f;

    [HideInInspector] public Matrix4x4 Ground = Matrix4x4.identity;
    [HideInInspector] public float pitch = -0.08f;
    float jumpHeight, jumpVelocity;

    /// <summary>False while something else (the golf ball camera) is driving the view.</summary>
    [System.NonSerialized] public bool inputEnabled = true;
    /// <summary>True while a menu is open: no looking, walking or capturing the mouse.</summary>
    [System.NonSerialized] public bool paused;
    /// <summary>Optional last word on the view, e.g. the golf flight camera.</summary>
    public System.Func<Matrix4x4, Matrix4x4> cameraFilter;
    /// <summary>Optional: is this ground point blocked (by laser puzzle pieces)?</summary>
    public System.Func<Vector4, bool> blocked;

    /// <summary>Frame count on which the cursor was captured (that click shouldn't also swing).</summary>
    public int LockedOnFrame { get; private set; } = -1;

    float R => S3World.Instance ? S3World.Instance.radius : 12f;
    public Vector4 Foot => Ground.GetColumn(3);
    public Vector4 Forward => Ground.GetColumn(2);
    public Vector4 Right => Ground.GetColumn(0);

    public Matrix4x4 CameraFrame =>
        Ground * S3.PlaneRotation(S3.EW, S3.EY, (eyeHeight + jumpHeight) / R) * S3.PlaneRotation(S3.EZ, S3.EY, pitch);

    void Start()
    {
        var cam = GetComponent<Camera>();
        transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 200f;
    }

    void Update()
    {
        if (paused) return;
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float dt = Time.deltaTime;
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (mouse != null)
        {
            if (!locked && mouse.leftButton.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                LockedOnFrame = Time.frameCount;
            }
            else if (locked && inputEnabled)
            {
                Vector2 d = mouse.delta.ReadValue();
                Turn(d.x * lookSensitivity);
                pitch = Mathf.Clamp(pitch + d.y * lookSensitivity, -1.5f, 1.5f);
            }
        }
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (!inputEnabled) return;

        float fwd = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
        float side = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
        if (fwd != 0 || side != 0)
        {
            float speed = walkSpeed * (kb.leftShiftKey.isPressed ? runMultiplier : 1) * dt;
            float n = Mathf.Sqrt(fwd * fwd + side * side);
            float dx = side / n * speed, dz = fwd / n * speed;
            Matrix4x4 before = Ground;
            Move(dx, dz);
            // refuse to step into a blocker (puzzle pieces), sliding along it if possible,
            // but always let the player out of one
            if (blocked != null && blocked(Foot) && !blocked(before.GetColumn(3)))
            {
                Ground = before; Move(dx, 0);
                if (blocked(Foot)) { Ground = before; Move(0, dz); }
                if (blocked(Foot)) Ground = before;
            }
        }

        if (jumpHeight <= 0 && kb.spaceKey.wasPressedThisFrame) jumpVelocity = jumpSpeed;
        if (jumpHeight > 0 || jumpVelocity > 0)
        {
            jumpVelocity -= gravity * dt;
            jumpHeight += jumpVelocity * dt;
            if (jumpHeight <= 0) { jumpHeight = 0; jumpVelocity = 0; }
        }

        Collide();
        Ground = S3.FrameAt(S3.Foot(Foot), S3.EY, Forward);   // keep the frame exactly orthonormal
    }

    void LateUpdate() => ApplyCamera();

    public void ApplyCamera()
    {
        if (S3World.Instance) S3World.Instance.SetCamera(ViewFrame());
    }

    /// <summary>The frame this camera actually renders from (after the golf flight camera, if any).</summary>
    public Matrix4x4 ViewFrame() => cameraFilter != null ? cameraFilter(CameraFrame) : CameraFrame;

    /// <summary>Turn right by angle radians.</summary>
    public void Turn(float angle) => Ground *= S3.PlaneRotation(S3.EZ, S3.EX, angle);

    /// <summary>Walk dx metres right and dz metres forward along a geodesic.</summary>
    public void Move(float dx, float dz)
    {
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d < 1e-7f) return;
        Ground *= S3.PlaneRotation(S3.EW, new Vector4(dx / d, 0, dz / d, 0), d / R);
    }

    void Collide()
    {
        var w = S3World.Instance;
        if (!w) return;
        foreach (var o in w.obstacles)
        {
            if (o.height < 0.45f || jumpHeight > o.height) continue;   // step / jump over low things
            float d = S3.Dist(Foot, o.foot) * R, min = o.radius + bodyRadius;
            if (d >= min || d < 1e-5f) continue;
            Vector4 away = -S3.DirTo(Foot, o.foot);
            float push = min - d;
            Move(Vector4.Dot(away, Right) * push, Vector4.Dot(away, Forward) * push);
        }
    }

    /// <summary>Stand at a ground point facing along a tangent direction.</summary>
    public void Teleport(Vector4 foot, Vector4 forward, float newPitch)
    {
        Ground = S3.FrameAt(S3.Foot(foot), S3.EY, forward);
        pitch = newPitch;
        jumpHeight = jumpVelocity = 0;
    }

    /// <summary>Pose by local ground coordinates around the origin (used by the capture tool).</summary>
    public void SetPose(float x, float z, float yawDeg, float pitchDeg)
    {
        Ground = S3.GroundFrame(x, z, yawDeg * Mathf.Deg2Rad, R);
        pitch = pitchDeg * Mathf.Deg2Rad;
    }
}
