# ScriptHookVDotNet3 API (from reflection on the installed DLL)

Source: `C:\Program Files\Rockstar Games\Grand Theft Auto V Enhanced\ScriptHookVDotNet3.dll`
(assembly ScriptHookVDotNet3 3.9.0.0, file version 3.9.0; it references ScriptHookVDotNet 3.9.0.5).
Full raw dump: `SHVDN_API_full_dump.txt` in this folder. `[Obsolete]` = compiles with a CS0618 warning.

## Raycast
- `static RaycastResult World.Raycast(Vector3 source, Vector3 target, IntersectFlags options, Entity ignoreEntity = null)`
- `static RaycastResult World.Raycast(Vector3 source, Vector3 direction, float maxDistance, IntersectFlags options, Entity ignoreEntity = null)`
- `static RaycastResult World.RaycastCapsule(Vector3 source, Vector3 target, float radius, IntersectFlags options, Entity ignoreEntity = null)` [Obsolete: use ShapeTest.StartTestCapsule]
- `static RaycastResult World.RaycastCapsule(Vector3 source, Vector3 direction, float maxDistance, float radius, IntersectFlags options, Entity ignoreEntity = null)` [Obsolete]
- `struct RaycastResult`: `bool DidHit`, `Vector3 HitPosition`, `Vector3 SurfaceNormal`, `Entity HitEntity`, `int Result`, `MaterialHash MaterialHash` (all get-only)
- `enum IntersectFlags`: Map=1, Vehicles=2, MissionEntities=2, PedCapsules=4, Ragdolls=8, Peds=12, Objects=16, LosToEntity=17, Pickups=32, Glass=64, BoundingBox=126, Rivers=128, Vegetation=256, Foliage=256, Everything=511
- Newer API: `static ShapeTestHandle ShapeTest.StartTestCapsule(Vector3 start, Vector3 end, float radius, IntersectFlags flags = Map, Entity excludeEntity = null, ShapeTestOptions options = IgnoreNoCollision)`;
  `StartTestLOSProbe(...)`, `StartExpensiveSyncTestLOSProbe(...)` (same-frame result), `StartTestSweptSphere(...)`, `StartTestBox(...)`.
  `ShapeTestHandle.GetResult(out ShapeTestResult)` -> `ShapeTestStatus`; also `(ShapeTestStatus, ShapeTestResult) GetResult()`.
  `struct ShapeTestResult`: `bool DidHit`, `Vector3 HitPosition`, `Vector3 SurfaceNormal`, `bool TryGetHitEntity(out Entity)`.
  `enum ShapeTestOptions`: IgnoreGlass=1, IgnoreSeeThrough=2, IgnoreNoCollision=4, Default=7.

## Cameras
- `static Camera World.CreateCamera(Vector3 position, Vector3 rotation, float fov)` [Obsolete: use Camera.Create]
- `static Camera World.RenderingCamera { get; set; }` [Obsolete: use ScriptCameraDirector]
- `static void World.DestroyAllCameras()` [Obsolete: use Camera.DeleteAllCameras]
- `static Camera Camera.Create(ScriptedCameraNameHash hash, Vector3 position, Vector3 rotation, float fov = 65, bool startActivated = false, EulerRotationOrder rotOrder = YXZ)` (+ string-name overload and overloads without position)
- `static void Camera.DeleteAllCameras(bool shouldApplyAcrossAllThreads = false)`
- `enum ScriptedCameraNameHash` (NOT "ScriptCameraNameHash"): DefaultScriptedCamera, DefaultSplineCamera, RoundedSplineCamera, DefaultAnimatedCamera, CustomTimedSplineCamera, TimedSplineCamera, DefaultTransitionCamera, SmoothedSplineCamera, DefaultScriptedFlyCamera
- `static class ScriptCameraDirector`: `StartRendering()`, `StartRenderingWithInterp(int, bool)`, `StopRendering(bool)`, `StopRenderingWithInterp(int, bool, bool)`, `Camera RenderingCam {get}`
- `Camera` members: `bool IsActive {get;set}`, `Vector3 Position {get;set}`, `Vector3 Rotation {get;set}`, `Vector3 Direction {get;set}`, `float FieldOfView {get;set}`, `float NearClip/FarClip {get;set}`, `Matrix Matrix {get}`, `Up/Right/ForwardVector {get}`,
  `void AttachTo(Entity entity, Vector3 offset)`, `void AttachTo(PedBone pedBone, Vector3 offset)`, `void AttachToVehicleBone(EntityBone, Vector3 positionOffset, bool offsetIsRelative = true)`, `void HardAttachToVehicleBone(EntityBone, Vector3 rotationOffset, Vector3 positionOffset, bool offsetIsRelative = true)`, `void Detach()`,
  `void Delete()`, `void Delete(bool shouldApplyAcrossAllThreads)`, `bool Exists()`, `void PointAt(...)`, `void Shake(CameraShake, float)`, implicit -> InputArgument.
  No HardAttachToEntity wrapper: use `Function.Call(Hash.HARD_ATTACH_CAM_TO_ENTITY, cam, ent, rx, ry, rz, ox, oy, oz, isRelative)`.

## Props / Model / Entity
- `static Prop World.CreateProp(Model model, Vector3 position, bool dynamic, bool placeOnGround)`
- `static Prop World.CreateProp(Model model, Vector3 position, Vector3 rotation, bool dynamic, bool placeOnGround)`
- `static Prop World.CreatePropNoOffset(Model model, Vector3 position, bool dynamic)` / `(Model, Vector3 position, Vector3 rotation, bool dynamic)`
- `struct Model`: ctors `(int hash)`, `(string name)`, `(PedHash)`, `(WeaponHash)`, `(VehicleHash)`; implicit from int/string/PedHash/...; `bool IsValid, IsInCdImage, IsLoaded`; `void Request()`, `bool Request(int timeout)`, `void MarkAsNoLongerNeeded()`, `(Vector3,Vector3) Dimensions`
- `Entity`: `Quaternion Quaternion {get;set}`, `Vector3 Position {get;set}`, `Vector3 PositionNoOffset {set}`, `Vector3 Rotation {get;set}`, `Matrix Matrix {get}`, `Vector3 Velocity {get;set}`, `Vector3 LocalRotationVelocity / WorldRotationVelocity {get;set}`, `float Speed {get;set}`,
  `bool IsCollisionEnabled {get;set}`, `bool IsPositionFrozen {get;set}`, `bool IsVisible {get;set}`, `bool HasGravity {get;set}`, `bool HasCollided {get}`, `bool IsInvincible {get;set}`, `int Opacity {get;set}`,
  `int Health {get;set}`, `float HealthFloat {get;set}`, `int MaxHealth`, `bool IsDead {get}`, `Model Model {get}`, `Up/Right/ForwardVector {get}`,
  `void Delete()`, `void MarkAsNoLongerNeeded()`, `bool Exists()`, `void SetNoCollision(Entity, bool)`,
  `void AttachTo(Entity entity, Vector3 position = default, Vector3 rotation = default)`, `void Detach()`,
  `void ApplyForce(Vector3 direction, Vector3 rotation = default, ForceType forceType = ForceType.ExternalImpulse)`, `void ApplyForceRelative(same)`,
  `void ApplyWorldForceCenterOfMass(Vector3 force, ForceType forceType, bool scaleByMass, bool applyToChildren = false)` (+ other ApplyWorld/RelativeForce* variants).
  **No `HasCollision` property** (use `IsCollisionEnabled`). `RotationVelocity` is [Obsolete].
- `enum ForceType`: InternalForce=0, InternalImpulse=1, ExternalForce=2, ExternalImpulse=3, Torque=4, AngularImpulse=5

## Math (GTA.Math)
- `Vector3`: fields X,Y,Z; `Vector3 Normalized {get}`, `float Length()`, `LengthSquared()`, `void Normalize()`, `DistanceTo(Vector3)`,
  `static Dot(Vector3, Vector3)`, `static Cross(Vector3, Vector3)`, `static Normalize`, `Lerp`, `Project`, `ProjectOnPlane`, `Reflect`, `Clamp`, `Angle`, `SignedAngle`; `Zero, UnitX/Y/Z, WorldUp`; operators + - unary- * (float both sides) /; implicit -> Vector2. **No implicit -> InputArgument** (pass X,Y,Z to natives).
- `Quaternion`: fields X,Y,Z,W; ctors `(x,y,z,w)`, `(Vector3 axis, float angle)`; `Identity`; `float Length()`, `void Normalize()`, `void Conjugate()`, `void Invert()`, `Vector3 Axis`, `float Angle`,
  `static RotationAxis(Vector3 axis, float angle)` (radians), `static RotationMatrix(Matrix)`, `static RotationYawPitchRoll(float yaw, float pitch, float roll)`, `static DirectionVectors(Vector3 right, Vector3 forward, Vector3 up)`, `static GetDirectionVectors(Quaternion, out right, out forward, out up)`,
  `static Euler(Vector3 euler[, EulerRotationOrder])` (degrees), `Vector3 ToEuler(EulerRotationOrder = YXZ)`, `static LookRotation(fwd[, up])`, `static FromToRotation`, `static Slerp/Lerp/Normalize/Conjugate/Invert/Multiply/Dot`,
  operators: `Quaternion * Quaternion`, `Quaternion * float`, `Quaternion * Vector3 -> Vector3` (rotate), `RotateTransform(Vector3)`.
- `Matrix` exists: M11..M44, `Identity`, `Inverse()`, `TransformPoint/TransformVector/InverseTransformVector`, `static RotationAxis`, `RotationQuaternion`, `RotationYawPitchRoll`, `Translation`, `Multiply`, operator *.
- `enum EulerRotationOrder`: XYZ, XZY, YXZ, YZX, ZXY, ZYX

## Game / Player / Controls
- `static Player Game.Player {get}`, `static Ped Game.LocalPlayerPed {get}`, `Player.Character` (Ped), `Player.CanControlCharacter {get;set}`, `Player.IsInvincible`, `Player.SetControlState(bool, SetPlayerControlFlags = None)`
- `static float Game.LastFrameTime {get}`, `static float Game.FPS {get}`, `static int Game.GameTime`, `static float Game.TimeScale {get;set}`, `static bool Game.IsPaused {get;set}`, `static bool Game.isEnhanced`
- `static void Game.DisableControlThisFrame(Control control)`, `static void Game.DisableAllControlsThisFrame()`, `EnableControlThisFrame(Control)`, `IsControlPressed/JustPressed(Control)`, `GetControlValueNormalized(Control)`, `GetDisabledControlValueNormalized(Control)`, `static bool Game.IsKeyPressed(Keys)`
- `enum GTA.Control` (namespace GTA, 365 values), e.g. NextCamera=0, LookLeftRight=1, LookUpDown=2, Sprint=21, Jump=22, Enter=23, Attack=24, Aim=25, Phone=27, MoveLeftRight=30, MoveUpDown=31, Duck=36, Reload=45, VehicleExit=75, FrontendPause=199, FrontendPauseAlternate=200

## UI (GTA.UI)
- `TextElement` ctors: `(string caption, PointF position, float scale)`, `(+ Color color)`, `(+ Font font)`, `(+ Alignment alignment)`, `(+ bool shadow, bool outline)`, `(+ float wrapWidth)`; props Caption, Position, Scale, Color, Font, Alignment, Shadow, Outline, Enabled, Centered;
  `void Draw()`, `Draw(SizeF offset)`, `ScaledDraw()`, `ScaledDraw(SizeF offset)`, `WorldDraw(Vector3)`. Coordinates are on the 1280x720 virtual canvas (ScaledDraw uses Screen.ScaledWidth).
- `ContainerElement(PointF position, SizeF size, Color color[, bool centered])`, `Draw()/ScaledDraw()`
- `Screen`: `static void ShowSubtitle(string message, int duration = 2500)`, `ShowSubtitle(string, int, bool drawImmediately = true)`, `static void FadeOut(int time)`, `FadeIn(int time)`, `IsFadedOut/IsFadingOut...`, `ShowHelpTextThisFrame(string)`, `PointF WorldToScreen(Vector3, bool scaleWidth = false)`, `static Size Resolution`, `float ScaledWidth`, `const Width/Height`
- `Notification`: `static int Show(string message, bool blinking = false)` [Obsolete: use PostTicker], `static FeedPost PostTicker(string message, bool isImportant, bool cacheMessage = true)`, `Hide(int)`
- `enum Font`: ChaletLondon=0, HouseScript=1, RockstarTag/Monospace=2, Leaderboard=3, ChaletComprimeCologne=4, FixedWidthNumbersStyle=5, ChaletComprimeCologneNotGamerName=6, Pricedown=7, Taxi=8
- `enum Alignment`: Center=0, Left=1, Right=2

## Script / Natives
- `GTA.Script`: `event EventHandler Tick`, `event EventHandler Aborted`, `event KeyEventHandler KeyDown`, `KeyUp`; `protected int Interval {get;set}` (protected, settable from subclass); `static void Wait(int ms)`, `static void Yield()`, `void Abort()`, `ScriptSettings Settings`, `string BaseDirectory`
- `GTA.Native.Function`: `static T Call<T>(Hash hash, params InputArgument[] args)` (+ 0..16 fixed-arg overloads), `static void Call(Hash hash, ...)` same shapes, `CallLongBlockingFunc[...]`
- `InputArgument` implicit from: bool, byte, sbyte, short, ushort, int, uint, long, ulong, float, double, string, IntPtr, Enum, OutputArgument, Char*, void*; plus Entity/Camera/Model/ShapeTestHandle provide their own implicit -> InputArgument. Vector3 does NOT.
- `OutputArgument`: `new OutputArgument()`, `T GetResult<T>()`, `Dispose()`
- `enum GTA.Native.Hash` (6460 members). Confirmed present: HARD_ATTACH_CAM_TO_ENTITY, ATTACH_CAM_TO_ENTITY, SET_CAM_ROT, RENDER_SCRIPT_CAMS, SET_ENTITY_QUATERNION, APPLY_DAMAGE_TO_PED, SET_ENTITY_COLLISION, FREEZE_ENTITY_POSITION, also SET_CAM_FOV, CREATE_CAM_WITH_PARAMS, SET_CAM_ACTIVE, DESTROY_CAM, DISABLE_CONTROL_ACTION, DISABLE_ALL_CONTROL_ACTIONS, START_SHAPE_TEST_CAPSULE, START_SHAPE_TEST_LOS_PROBE, START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE, GET_SHAPE_TEST_RESULT, SET_ENTITY_COORDS_NO_OFFSET, SET_ENTITY_VELOCITY, SET_ENTITY_ROTATION, SET_CAM_NEAR_CLIP, SET_FOCUS_POS_AND_VEL, CLEAR_FOCUS, ADD_EXPLOSION, SET_CAM_AFFECTS_AIMING, SET_CAM_CONTROLS_MINI_MAP_HEADING, ATTACH_CAM_TO_PED_BONE.

## Ped / Vehicle
- `Ped`: `int Health {get;set}` / `float HealthFloat` / `bool IsDead` (inherited from Entity), `int Armor`, `bool IsPlayer`, `bool IsInVehicle()`, `bool IsInVehicle(Vehicle)`, `Vehicle CurrentVehicle {get}`, `void ApplyDamage(int damageAmount)`, `void Kill()`, `void Ragdoll(int duration = -1, RagdollType = Relax)`, `bool IsRagdoll`, `bool CanRagdoll`
- `Vehicle`: `void ApplyDamage(Vector3 position, float damageAmount, float radius)`, `void ApplyDamageDeformation(Vector3 position, float damage, float deformation, bool localDamage)`, `void Explode()`, `float BodyHealth/EngineHealth/PetrolTankHealth/HeliEngineHealth {get;set}`, `HealthFloat` (inherited), `bool IsDamaged`
