using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Globalization;

namespace MatchZy;

public static class PlayerTeleport
{
    public static bool TeleportSafely(CCSPlayerController? player, Vector position, QAngle savedViewAngle)
    {
        if (player == null || !player.IsValid || !player.PlayerPawn.IsValid)
        {
            return false;
        }

        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        QAngle bodyAngle = new(0.0f, savedViewAngle.Y, 0.0f);
        Vector zeroVelocity = new(0.0f, 0.0f, 0.0f);

        // Keep the physical pawn upright, matching the spawn teleport behavior.
        pawn.Teleport(position, bodyAngle, zeroVelocity);

        // setang changes the player's view without applying pitch or roll to the
        // physical model. The command name is fixed and every argument is a
        // locally formatted numeric angle captured from the same player.
        string viewAngleCommand = string.Create(
            CultureInfo.InvariantCulture,
            $"setang {savedViewAngle.X:R} {savedViewAngle.Y:R} {savedViewAngle.Z:R}");
        player.ExecuteClientCommandFromServer(viewAngleCommand);

        return true;
    }

    // Teleports a live T/CT player to a saved position (lineups, .last, .back, .loadpos). The view keeps the full angle, but the
    // body is kept upright: teleporting with a steep pitch otherwise tilts the player model. Returns false for dead players and
    // spectators, which are not moved.
    public static bool TeleportUpright(CCSPlayerController? player, Vector position, QAngle angle)
    {
        if (player == null || !player.IsValid || !player.PawnIsAlive) return false;
        if (player.TeamNum != (byte)CsTeam.Terrorist && player.TeamNum != (byte)CsTeam.CounterTerrorist) return false;
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return false;

        pawn.Teleport(position, angle, new Vector(0, 0, 0));
        // The teleport rotation is re-applied for a few ticks, so a single write would be overwritten.
        KeepBodyUpright(player, angle.Y, 6);
        return true;
    }

    private static void KeepBodyUpright(CCSPlayerController player, float yaw, int frames)
    {
        if (frames <= 0 || player == null || !player.IsValid) return;
        var node = player.PlayerPawn.Value?.CBodyComponent?.SceneNode;
        if (node == null) return;
        node.AbsRotation.X = 0;
        node.AbsRotation.Y = yaw;
        node.AbsRotation.Z = 0;
        Server.NextFrame(() => KeepBodyUpright(player, yaw, frames - 1));
    }
}
