using Mono.Cecil.Cil;
using MonoMod.Cil;

namespace moe.sylvi.SaveItemRotations.Features;

public static class FixItemDrop
{
	public static int Apply(int orig, int actualRotation)
	{
		return actualRotation;
	}

	public static class Patches
	{
		public static void Initialize()
		{
			IL.GameNetcodeStuff.PlayerControllerB.ThrowObjectClientRpc += PlayerControllerB_ThrowObjectClientRpc;
		}

		private static void PlayerControllerB_ThrowObjectClientRpc(ILContext il)
		{
			var cursor = new ILCursor(il);

			if (!cursor.TryGotoNext(MoveType.After, instr => instr.MatchLdcI4(-1)))
			{
				// Normally error about failed IL hooks but this only occurs when a mod does the same thing,
				// and erroring/warning here makes users concerned about the mod's functionality (it works fine)
				return;
			}

			cursor.Emit(OpCodes.Ldarg, 5);
			cursor.EmitDelegate(Apply);
		}
	}
}
