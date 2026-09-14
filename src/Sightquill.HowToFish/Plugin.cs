using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Sightquill.Bridge;
using UnityEngine;

namespace Sightquill.HowToFish;

[BepInPlugin("local.sightquill.howtofish", "Sightquill How to Fish", "0.2.1")]
[BepInProcess("How to Fish.exe")]
[DefaultExecutionOrder(32000)]
public sealed class Plugin : BaseUnityPlugin
{
    private readonly Dictionary<(Type, string), MemberInfo> members = new Dictionary<(Type, string), MemberInfo>();
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private AimPublisher? publisher;
    private Type? playerType;
    private Type? gameInfoType;
    private Type? pauseType;
    private Type? thinkingType;
    private Type? menuType;
    private int processId;
    private long sequence;
    private float nextErrorLog;
    private bool announced;

    private void Awake()
    {
        processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        playerType = assembly?.GetType("Player"); gameInfoType = assembly?.GetType("GameInfo");
        pauseType = assembly?.GetType("PauseManager"); thinkingType = assembly?.GetType("PlayerThinking"); menuType = assembly?.GetType("MainMenuManager");
        if (playerType == null || gameInfoType == null || pauseType == null || thinkingType == null || menuType == null) { Logger.LogError("Game API unavailable; Sightquill companion disabled."); enabled = false; return; }
        publisher = new AimPublisher();
        Logger.LogInfo("Local aim companion ready. No weapon, camera, input or projectile state is modified.");
    }

    private void LateUpdate()
    {
        if (publisher == null || playerType == null || gameInfoType == null) return;
        var x = .5f; var y = .5f; var kind = AimKind.Hidden;
        try
        {
            var player = Read(playerType, null, "LocalPlayer") as Component;
            var camera = Read(gameInfoType, null, "CurCamera") as Camera;
            // Menus, death cameras, Alt+Tab and unloaded scenes must not leave a false marker.
            if (player && camera && AimVisibility.ShouldShow(Application.isFocused, Cursor.lockState == CursorLockMode.Locked,
                (bool)Read(playerType, null, "LocalPlayerEnabled")!, (bool)Read(Read(player!, "Dying")!, "IsDead")!,
                (bool)Read(pauseType!, null, "IsPaused")!, (bool)Read(thinkingType!, null, "IsThinking")!, (bool)Read(menuType!, null, "IsInMenu")!))
            {
                kind = AimKind.Camera;
                var item = Read(Read(player!, "Holding")!, "HeldItem") as Component;
                var weapon = item ? Read(item!, "Weapon") as Component : null;
                if (weapon)
                {
                    var attachments = Read(weapon!, "Attachments")!;
                    var scope = (bool)Read(attachments, "UseSniperUi")! && (float)Read(weapon!, "_aimPercent")! > .9f;
                    var muzzle = Read(attachments, "FirePoint") as Transform;
                    if (!muzzle) throw new MissingMemberException("FirePoint unavailable");
                    var origin = scope ? camera!.transform.position : muzzle!.position;
                    var direction = scope ? camera!.transform.forward : muzzle!.forward;
                    kind = scope ? AimKind.Scope : AimKind.Barrel;
                    // First surface on the unmodified nominal axis, excluding the local body and held item.
                    // This does not predict random spread, projectile drop or target motion.
                    var distance = 1000f;
                    var mask = ((LayerMask)Read(gameInfoType, null, "ProjectileHitLayer")!).value;
                    var count = Physics.RaycastNonAlloc(origin, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
                    if (count == hits.Length) kind = AimKind.Hidden; // Saturated query has no guaranteed nearest hit.
                    for (var i = 0; i < count; i++)
                    {
                        var hit = hits[i];
                        if (!hit.collider || hit.transform.IsChildOf(player!.transform) || (item && hit.transform.IsChildOf(item!.transform))) continue;
                        if (hit.distance > .01f && hit.distance < distance) distance = hit.distance;
                    }
                    var screen = camera!.WorldToScreenPoint(origin + direction * distance);
                    if (Screen.width > 0 && Screen.height > 0 && screen.z > 0)
                    { x = screen.x / Screen.width; y = 1 - screen.y / Screen.height; }
                    else kind = AimKind.Hidden;
                    if (x < 0 || x > 1 || y < 0 || y > 1 || float.IsNaN(x) || float.IsNaN(y)) kind = AimKind.Hidden;
                }
                if (!announced) { Logger.LogInfo("Local player detected; aim samples are being published."); announced = true; }
            }
        }
        catch (Exception ex)
        {
            kind = AimKind.Hidden;
            if (Time.unscaledTime >= nextErrorLog) { Logger.LogWarning("Aim unavailable: " + ex.GetBaseException().Message); nextErrorLog = Time.unscaledTime + 5; }
        }
        if (kind == AimKind.Hidden) { x = .5f; y = .5f; }
        publisher.Publish(new AimFrame(processId, ++sequence, DateTime.UtcNow.Ticks, x, y, kind));
    }
    private object? Read(object instance, string name) => Read(instance.GetType(), instance, name);
    private object? Read(Type type, object? instance, string name)
    {
        var key = (type, name);
        if (!members.TryGetValue(key, out var member))
        {
            for (var search = type; search != null && member == null; search = search.BaseType)
                member = (MemberInfo?)search.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    ?? search.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (member == null) throw new MissingMemberException(type.Name, name);
            members[key] = member;
        }
        return member is PropertyInfo property ? property.GetValue(instance, null) : ((FieldInfo)member).GetValue(instance);
    }
    private void OnDestroy() { publisher?.Dispose(); publisher = null; }
}
