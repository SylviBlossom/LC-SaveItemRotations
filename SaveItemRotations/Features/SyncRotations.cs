using LethalNetworkAPI;
using LethalNetworkAPI.Utils;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using static Netcode.Transports.Facepunch.FacepunchTransport;
using Object = UnityEngine.Object;

namespace moe.sylvi.SaveItemRotations.Features;

public static class SyncRotations
{
	public static LNetworkEvent RequestSyncEvent = LNetworkEvent.Connect("RequestItemSync", onServerReceived: OnRequestSync);
	public static LNetworkMessage<List<ItemData>> SyncItemMessage = LNetworkMessage<List<ItemData>>.Connect("SyncItemData", onClientReceived: OnReceiveSync);

	private static void OnReceiveSync(List<ItemData> dataList)
	{
		if (!Config.Instance.SyncOnLoad.Value)
		{
			Plugin.Logger.LogInfo($"Sync | Got item sync from server but SyncOnLoad is disabled, ignoring");
			return;
		}

		Plugin.Logger.LogInfo($"Sync | Got item sync from server with {dataList.Count} object(s)");

		foreach (var itemData in dataList)
		{
			if (!itemData.NetworkObject.TryGet(out var networkObject))
			{
				Plugin.Logger.LogWarning($"Sync | Unknown object reference {itemData.NetworkObject.NetworkObjectId}");
				Plugin.Logger.LogWarning($"Sync |   - Supplied rotation: {itemData.EulerAngles}");
				continue;
			}

			var grabbableObject = networkObject.gameObject.GetComponent<GrabbableObject>();
			if (grabbableObject != null && IsValidObject(grabbableObject))
			{
				ApplyRotationTo(grabbableObject, itemData.EulerAngles);
			}
			else
			{
				Plugin.Logger.LogWarning($"Sync | Attempted to sync invalid item {itemData.NetworkObject.NetworkObjectId}");
			}
		}
	}

	private static void OnRequestSync(ulong clientId)
	{
		if (!Config.Instance.SyncOnLoad.Value)
		{
			Plugin.Logger.LogInfo($"Sync | Got item sync request from client {clientId} but SyncOnLoad is disabled, ignoring");
			return;
		}

		Plugin.Logger.LogInfo($"Sync | Got item sync request from client {clientId}");

		var itemData = Object.FindObjectsOfType<GrabbableObject>().Where(IsValidObject).Select(grabbableObject => new ItemData
		{
			NetworkObject = grabbableObject.NetworkObject,
			EulerAngles = grabbableObject.transform.eulerAngles
		});

		SyncItemMessage.SendClient(itemData.ToList(), clientId);
	}

	public static void InitializeNetworkingAndSync()
	{
		if (NetworkManager.Singleton.IsHost)
		{
			return;
		}

		if (!Config.Instance.SyncOnLoad.Value)
		{
			Plugin.Logger.LogInfo($"Sync | SyncOnLoad is disabled, skipping item sync");
			return;
		}

		Plugin.Logger.LogInfo($"Sync | Requesting item sync");

		RequestSyncEvent.InvokeServer();
	}

	private static bool IsValidObject(GrabbableObject grabbableObject)
	{
		return !grabbableObject.isHeld && grabbableObject.parentObject == null && grabbableObject.reachedFloorTarget;
	}

	private static void ApplyRotationTo(GrabbableObject grabbableObject, Vector3 eulerAngles)
	{
		grabbableObject.floorYRot = -1;
		grabbableObject.transform.rotation = Quaternion.Euler(grabbableObject.transform.eulerAngles.x, eulerAngles.y, grabbableObject.transform.eulerAngles.z);
	}

	public class ItemData
	{
		public NetworkObjectReference NetworkObject;
		public Vector3 EulerAngles;
	}

	public static class Patches
	{
		public static void Initialize()
		{
			On.GameNetcodeStuff.PlayerControllerB.ConnectClientToPlayerObject += PlayerControllerB_ConnectClientToPlayerObject;
		}

		private static void PlayerControllerB_ConnectClientToPlayerObject(On.GameNetcodeStuff.PlayerControllerB.orig_ConnectClientToPlayerObject orig, GameNetcodeStuff.PlayerControllerB self)
		{
			orig(self);

			InitializeNetworkingAndSync();
		}
	}
}
