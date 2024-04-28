﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AsksvinImproved
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class AsksvinImprovedPlugin : BaseUnityPlugin
    {
        internal const string ModName = "AsksvinImproved";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = $"{Author}.{ModName}";
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource AsksvinImprovedLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
    static class PlayerSetLocalPlayerPatch
    {
        public static bool TolerateFire;

        static void Postfix(Player __instance)
        {
            TolerateFire = __instance.m_tolerateFire;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartDoodadControl))]
    static class PlayerStartDoodadControlPatch
    {
        public static bool RidingAsksvin;
        public static Humanoid RidingHumanoid = null!;

        static void Postfix(Player __instance, IDoodadController shipControl)
        {
#if DEBUG
            AsksvinImprovedPlugin.AsksvinImprovedLogger.LogDebug($"PlayerIsRidingPatch: They are on {Utils.GetPrefabName(__instance.m_doodadController.GetControlledComponent().gameObject.name)}");
#endif
            if (Utils.GetPrefabName(shipControl.GetControlledComponent().gameObject.name) == "Asksvin")
            {
                __instance.m_tolerateFire = true;
                RidingAsksvin = true;
                RidingHumanoid = shipControl.GetControlledComponent().transform.GetComponentInParent<Humanoid>();
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StopDoodadControl))]
    static class PlayerStopDoodadControlPatch
    {
        static bool Prefix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid())
            {
                // Ensure dismount if the mount dies
                PlayerStartDoodadControlPatch.RidingAsksvin = false;
                PlayerStartDoodadControlPatch.RidingHumanoid = null!;
                __instance.m_tolerateFire = PlayerSetLocalPlayerPatch.TolerateFire;
                return true;
            }


            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    static class HumanoidStartAttackPatch
    {
        static bool Prefix(Humanoid __instance)
        {
            if (__instance != Player.m_localPlayer) return true;
            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.AttachStop))]
    static class PlayerAttachStopPatch
    {
        static bool Prefix(Player __instance)
        {
            return !PlayerStartDoodadControlPatch.RidingAsksvin;
        }
    }


    [HarmonyPatch(typeof(Player), nameof(Player.UpdateDoodadControls))]
    static class PlayerUpdateDoodadControlsPatch
    {
        static void Postfix(Player __instance)
        {
            if (__instance.m_doodadController == null || !__instance.m_doodadController.IsValid() || !PlayerStartDoodadControlPatch.RidingAsksvin)
                return;
            __instance.m_tolerateFire = true;

            // Check if the mount is dead
            if (PlayerStartDoodadControlPatch.RidingHumanoid?.GetHealth() <= 0)
            {
                __instance.CustomAttachStop();
                return;
            }

            // Detect and handle jump input specifically for dismounting
            if (Input.GetKeyDown(KeyCode.Space) || ZInput.GetButtonDown("JoyJump"))
            {
                __instance.CustomAttachStop();
                return;
            }

            __instance.HandleInput();
        }
    }

    public static class PlayerExtensions
    {
        public static void CustomAttachStop(this Player p)
        {
            if (p.m_sleeping || !p.m_attached)
                return;
            if (p.m_attachPoint != null)
                p.transform.position = p.m_attachPoint.TransformPoint(p.m_detachOffset);
            if (p.m_attachColliders != null)
            {
                foreach (Collider attachCollider in p.m_attachColliders)
                {
                    if (attachCollider)
                        Physics.IgnoreCollision(p.m_collider, attachCollider, false);
                }

                p.m_attachColliders = null;
            }

            p.m_body.useGravity = true;
            p.m_attached = false;
            p.m_attachPoint = null;
            p.m_attachPointCamera = null;
            p.m_zanim.SetBool(p.m_attachAnimation, false);
            p.m_nview.GetZDO().Set(ZDOVars.s_inBed, false);
            p.ResetCloth();
            p.m_doodadController = null;
            p.StopDoodadControl();
        }

        public static void HandleInput(this Player player)
        {
            void ProcessInput(KeyCode key, int weaponIndex)
            {
                if (!PlayerStartDoodadControlPatch.RidingHumanoid || !Input.GetKeyDown(key) || Menu.IsVisible() || !player.TakeInput()) return;
                if (PlayerStartDoodadControlPatch.RidingHumanoid.InAttack())
                    return;

                List<ItemDrop.ItemData> items = PlayerStartDoodadControlPatch.RidingHumanoid.m_inventory.GetAllItems().Where(i => i.IsWeapon()).ToList();

                if (items.Count <= weaponIndex) return;
                ItemDrop.ItemData weapon = items[weaponIndex];
                PlayerStartDoodadControlPatch.RidingHumanoid.EquipItem(weapon);
                PlayerStartDoodadControlPatch.RidingHumanoid.StartAttack(null, false);
            }
            
            ProcessInput(KeyCode.Mouse0, 0); // Left click
            ProcessInput(KeyCode.Mouse1, 1); // Right click
            ProcessInput(KeyCode.Mouse2, 2); // Middle click
        }
    }
}