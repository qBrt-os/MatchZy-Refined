
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace MatchZy;
public partial class MatchZy
{
    public HookResult EventPlayerConnectFullHandler(EventPlayerConnectFull @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            if (!IsPlayerValid(player)) return HookResult.Continue;
            Log($"[FULL CONNECT] Player ID: {player!.UserId}, Name: {player.PlayerName} has connected!");

            // Handling whitelisted players
            if (!player.IsBot || !player.IsHLTV)
            {
                var steamId = player.SteamID;

                bool kicked = HandlePlayerWhitelist(player, steamId.ToString());
                if (kicked) return HookResult.Continue;

                if (isMatchSetup || matchModeOnly)
                {
                    CsTeam team = GetPlayerTeam(player);
                    if (team == CsTeam.None)
                    {
                        Log($"[EventPlayerConnectFull] KICKING PLAYER STEAMID: {steamId}, Name: {player.PlayerName} (NOT ALLOWED!)");
                        PrintToAllChat($"Kicking player {player.PlayerName} - Not a player in this game.");
                        KickPlayer(player);
                        return HookResult.Continue;
                    }
                }
            }

            if (player.UserId.HasValue)
            {
                playerData[player.UserId.Value] = player;
                connectedPlayers++;
                if (readyAvailable && !matchStarted)
                {
                    playerReadyStatus[player.UserId.Value] = false;
                }
                else
                {
                    playerReadyStatus[player.UserId.Value] = true;
                }
            }

            // Practice disables the engine's team-wide respawn cvars so bot respawning can
            // be controlled independently. A newly connected human therefore needs the same
            // explicit respawn path used for human deaths.
            EnableDefaultHumanLifeRegeneration(player);
            SchedulePracticeHumanRespawn(player, PracticeRespawnDelaySeconds);
            // May not be required, but just to be on safe side so that player data is properly updated in dictionaries
            // Update: Commenting the below function as it was being called multiple times on map change.
            // UpdatePlayersMap();

            if (readyAvailable && !matchStarted)
            {
                // Start Warmup when first player connect and match is not started.
                if (GetRealPlayersCount() == 1)
                {
                    Log($"[FULL CONNECT] First player has connected, starting warmup!");
                    ExecUnpracCommands();
                    AutoStart();
                }
            }
            return HookResult.Continue;

        }
        catch (Exception e)
        {
            Log($"[EventPlayerConnectFull FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }

    }
    public HookResult EventPlayerDisconnectHandler(EventPlayerDisconnect @event, GameEventInfo info)
    {
        try
        {
            CCSPlayerController? player = @event.Userid;

            // The controller can already be invalid by the time this event is handled.
            // UserId is still the key for all session-scoped practice data, so clear it
            // even when the pawn/controller can no longer be used.
            if (player == null || !player.UserId.HasValue) return HookResult.Continue;
            int userId = player.UserId.Value;

            CloseConfigurationMenu(userId, clearDisplay: false);

            if (playerReadyStatus.ContainsKey(userId))
            {
                playerReadyStatus.Remove(userId);
                connectedPlayers--;
            }
            playerData.Remove(userId);
            infernoStartTimes.Remove(userId);
            pracUsedBots.Remove(userId);

            if (matchzyTeam1.coach.Contains(player))
            {
                matchzyTeam1.coach.Remove(player);
                SetPlayerVisible(player);
                player.Clan = "";
            }
            else if (matchzyTeam2.coach.Contains(player))
            {
                matchzyTeam2.coach.Remove(player);
                SetPlayerVisible(player);
                player.Clan = "";
            }
            noFlashList.Remove(userId);
            practiceSwitchNoDeath.Remove(userId);
            if (playerTimers.TryGetValue(userId, out var practiceTimer))
            {
                practiceTimer.KillTimer();
                playerTimers.Remove(userId);
            }
            lastGrenadesData.Remove(userId);
            nadeSpecificLastGrenadeData.Remove(userId);
            savedPlayerLocationData.Remove(userId);
            humanGodModeEnabled.Remove(userId);
            humanLifeRegenerationEnabled.Remove(userId);
            humanNextRegenerationTimes.Remove(userId);
            lastRethrowCommandTime.Remove(userId);
            lastGlobalRethrowCommandTime.Remove(userId);

            HandleVetoCaptainLeft(userId);

            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDisconnect FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventCsWinPanelRoundHandler(EventCsWinPanelRound @event, GameEventInfo info)
    {
        // EventCsWinPanelRound has stopped firing after Arms Race update, hence we handle knife round winner in EventRoundEnd.

        // Log($"[EventCsWinPanelRound PRE] finalEvent: {@event.FinalEvent}");
        // if (isKnifeRound && matchStarted)
        // {
        //     HandleKnifeWinner(@event);
        // }
        return HookResult.Continue;
    }

    public HookResult EventCsWinPanelMatchHandler(EventCsWinPanelMatch @event, GameEventInfo info)
    {
        try
        {
            HandleMatchEnd();
            // ResetMatch();
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventCsWinPanelMatch FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundStartHandler(EventRoundStart @event, GameEventInfo info)
    {
        try
        {
            StartPendingDemoRecording();
            ResetPracticeRoundTimeout();
            RestoreTrackedPracticeBotsAfterRoundStart();
            AssignUniquePracticeHumanSpawnsAfterRoundStart();
            HandlePostRoundStartEvent(@event);
            RefreshPracticeSpawnMarkersAfterRoundStart();
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundStart FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventRoundFreezeEndHandler(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        try
        {
            if (!matchStarted) return HookResult.Continue;
            HashSet<CCSPlayerController> coaches = GetAllCoaches();

            foreach (var coach in coaches)
            {
                if (!IsPlayerValid(coach)) continue;
                // If coaches are still left alive after freezetime ends, this code will force them to spectate their team again.
                if (coach.PlayerPawn.Value?.LifeState != (byte)LifeState_t.LIFE_ALIVE) continue;

                Position coachPosition = new(coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsOrigin, coach.PlayerPawn.Value!.CBodyComponent!.SceneNode!.AbsRotation);
                PlayerTeleport.TeleportSafely(
                    coach,
                    new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f),
                    coachPosition.PlayerAngle);
                AddTimer(1.5f, () =>
                {
                    PlayerTeleport.TeleportSafely(
                        coach,
                        new Vector(coachPosition.PlayerPosition.X, coachPosition.PlayerPosition.Y, coachPosition.PlayerPosition.Z + 20.0f),
                        coachPosition.PlayerAngle);
                    CsTeam oldTeam = GetCoachTeam(coach);
                    coach.ChangeTeam(CsTeam.Spectator);
                    AddTimer(0.01f, () => coach.ChangeTeam(oldTeam));
                });
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventRoundFreezeEnd FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventPlayerGivenC4(EventPlayerGivenC4 @event, GameEventInfo info) {
        try {
            if (!matchStarted) return HookResult.Continue;
            if (@event.Userid == null) return HookResult.Continue;
            var recv = @event.Userid;

            // check if coach
            var coaches = reverseTeamSides["TERRORIST"].coach;
            if (coaches.Contains(recv)) {
                TransferCoachBomb(recv);
            }
        } catch (Exception e) {
            Log($"[EventPlayerGivenC4 FATAL] An error occured: {e.Message}");
        }
        return HookResult.Continue;
    }

    public void OnEntitySpawnedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || entity == null || entity.Entity == null) return;
            if (!Constants.ProjectileTypeMap.ContainsKey(entity.Entity.DesignerName)) return;

            // Looked up again by index next frame: the entity may be freed by then, and a stale handle must not be read.
            uint entityIndex = entity.Index;
            string designerName = entity.Entity.DesignerName;
            string nadeType = Constants.ProjectileTypeMap[designerName];
            Server.NextFrame(() => {
                try
                {
                    var projectile = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)entityIndex);
                    if (projectile == null || !projectile.IsValid || projectile.DesignerName != designerName) return;
                    // Spawned by a rethrow: gets the smoke color and detonation time below, but is not recorded into the history.
                    bool isRethrow = projectile.Globalname == "custom";
                    if (!projectile.Thrower.IsValid || projectile.Thrower.Value == null || projectile.Thrower.Value.Controller.Value == null) return;

                    CCSPlayerController player = new(projectile.Thrower.Value.Controller.Value.Handle);
                    if (!player.IsValid || !player.UserId.HasValue || !player.PlayerPawn.IsValid || player.PlayerPawn.Value == null) return;
                    var playerOrigin = player.PlayerPawn.Value.CBodyComponent?.SceneNode?.AbsOrigin;
                    if (playerOrigin == null || projectile.AbsOrigin == null || projectile.AbsRotation == null)
                    {
                        Log($"[OnEntitySpawnedHandler] {nadeType} of {player.PlayerName} not recorded: position not available.");
                        return;
                    }
                    int client = player.UserId.Value;
                    uint projectileIndex = projectile.Index;

                    Vector position = new(projectile.AbsOrigin.X, projectile.AbsOrigin.Y, projectile.AbsOrigin.Z);
                    QAngle angle = new(projectile.AbsRotation.X, projectile.AbsRotation.Y, projectile.AbsRotation.Z);
                    QAngle angularVelocity = new(projectile.AngVelocity.X, projectile.AngVelocity.Y, projectile.AngVelocity.Z);
                    Vector playerPosition = new(playerOrigin.X, playerOrigin.Y, playerOrigin.Z);
                    QAngle playerAngle = new(player.PlayerPawn.Value.EyeAngles.X, player.PlayerPawn.Value.EyeAngles.Y, player.PlayerPawn.Value.EyeAngles.Z);
                    ushort itemIndex = projectile.ItemIndex;

                    lastGrenadeThrownTime[(int)projectileIndex] = (DateTime.Now, client);
                    if (smokeColorEnabled.Value && nadeType == "smoke")
                    {
                        CSmokeGrenadeProjectile smokeProjectile = new(projectile.Handle);
                        smokeProjectile.SmokeColor.X = GetPlayerTeammateColor(player).R;
                        smokeProjectile.SmokeColor.Y = GetPlayerTeammateColor(player).G;
                        smokeProjectile.SmokeColor.Z = GetPlayerTeammateColor(player).B;
                    }
                    if (isRethrow) return;

                    // The launch velocity is in InitialVelocity; AbsVelocity can still read ~0 one frame after spawn.
                    Vector velocity = new(projectile.InitialVelocity.X, projectile.InitialVelocity.Y, projectile.InitialVelocity.Z);
                    if (!IsMovingVelocity(velocity)) velocity = new(projectile.AbsVelocity.X, projectile.AbsVelocity.Y, projectile.AbsVelocity.Z);
                    if (IsMovingVelocity(velocity))
                    {
                        RecordThrownGrenade(client, nadeType, position, angle, velocity, angularVelocity, playerPosition, playerAngle, itemIndex);
                        return;
                    }

                    // Neither velocity is set yet: work it out from how far the projectile moves in the next frame.
                    Server.NextFrame(() =>
                    {
                        var moved = Utilities.GetEntityFromIndex<CBaseCSGrenadeProjectile>((int)projectileIndex);
                        if (moved == null || !moved.IsValid || moved.AbsOrigin == null)
                        {
                            Log($"[OnEntitySpawnedHandler] {nadeType} of {player.PlayerName} not recorded: projectile gone before its velocity was known.");
                            return;
                        }
                        float tickRate = 1.0f / Server.TickInterval;
                        Vector recovered = new((moved.AbsOrigin.X - position.X) * tickRate, (moved.AbsOrigin.Y - position.Y) * tickRate, (moved.AbsOrigin.Z - position.Z) * tickRate);
                        if (!IsMovingVelocity(recovered))
                        {
                            Log($"[OnEntitySpawnedHandler] {nadeType} of {player.PlayerName} not recorded: no launch velocity.");
                            return;
                        }
                        RecordThrownGrenade(client, nadeType, position, angle, recovered, angularVelocity, playerPosition, playerAngle, itemIndex);
                    });
                }
                catch (Exception e)
                {
                    Log($"[OnEntitySpawnedHandler] {nadeType} not recorded: {e.Message}");
                }
            });
        }
        catch (Exception e)
        {
            Log($"[OnEntitySpawnedHandler FATAL] An error occurred: {e.Message}");
        }
    }

    // A real throw is always faster than 50 u/s; slower means the velocity was read before the engine set it.
    private static bool IsMovingVelocity(Vector velocity)
    {
        return velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z >= 2500f;
    }

    private void RecordThrownGrenade(int client, string nadeType, Vector position, QAngle angle, Vector velocity, QAngle angularVelocity, Vector playerPosition, QAngle playerAngle, ushort itemIndex)
    {
        GrenadeThrownData lastGrenadeThrown = new(position, angle, velocity, angularVelocity, playerPosition, playerAngle, nadeType, DateTime.Now, itemIndex);

        if (!lastGrenadesData.ContainsKey(client)) lastGrenadesData[client] = new();
        if (!nadeSpecificLastGrenadeData.ContainsKey(client)) nadeSpecificLastGrenadeData[client] = new();

        nadeSpecificLastGrenadeData[client][nadeType] = lastGrenadeThrown;
        lastGrenadesData[client].Add(lastGrenadeThrown);

        if (maxLastGrenadesSavedLimit != 0 && lastGrenadesData[client].Count > maxLastGrenadesSavedLimit)
        {
            lastGrenadesData[client].RemoveAt(0);
        }
    }

    public HookResult EventPlayerDeathPreHandler(EventPlayerDeath @event, GameEventInfo info)
    {
        try
        {
            // We do not broadcast the suicide of the coach
            if (!matchStarted) return HookResult.Continue;

            if (@event.Attacker == @event.Userid)
            {
                if (matchzyTeam1.coach.Contains(@event.Attacker!) || matchzyTeam2.coach.Contains(@event.Attacker!))
                {
                    info.DontBroadcast = true;
                }
            }
            return HookResult.Continue;
        }
        catch (Exception e)
        {
            Log($"[EventPlayerDeathPreHandler FATAL] An error occurred: {e.Message}");
            return HookResult.Continue;
        }
    }

    public HookResult EventSmokegrenadeDetonateHandler(EventSmokegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var data)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.smoke", player!.PlayerName, $"{(DateTime.Now - data.Time).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventFlashbangDetonateHandler(EventFlashbangDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var data)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.flash", player!.PlayerName, $"{(DateTime.Now - data.Time).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventHegrenadeDetonateHandler(EventHegrenadeDetonate @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var data)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.grenade", player!.PlayerName, $"{(DateTime.Now - data.Time).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }

    public HookResult EventInfernoStartburnHandler(EventInfernoStartburn @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;

        var inferno = Utilities.GetEntityFromIndex<CInferno>(@event.Entityid);
        if (inferno == null || !inferno.IsValid) return HookResult.Continue;

        var owner = inferno.OwnerEntity?.Value;
        if (owner == null || owner.DesignerName != "player") return HookResult.Continue;

        var pawn = new CCSPlayerPawn(owner.Handle);
        var controller = pawn.Controller?.Value;
        if (controller == null) return HookResult.Continue;

        var player = new CCSPlayerController(controller.Handle);
        if (!IsPlayerValid(player)) return HookResult.Continue;

        infernoStartTimes[player.UserId!.Value] = (DateTime.Now, inferno.SourceItemDefIndex == 48);
        return HookResult.Continue;
    }

    public void OnEntityDeletedHandler(CEntityInstance entity)
    {
        try
        {
            if (!isPractice || isDryRun) return;
            if (entity == null || !entity.IsValid) return;
            string designerName = entity.DesignerName;
            if (designerName == "molotov_projectile" || designerName == "incendiary_projectile")
            {
                if (lastGrenadeThrownTime.TryGetValue((int)entity.Index, out var data))
                {
                    var player = Utilities.GetPlayerFromUserid(data.Client);
                    if (player != null && IsPlayerValid(player))
                    {
                        Server.NextFrame(() =>
                        {
                            if (!IsPlayerValid(player)) return;
                            if (infernoStartTimes.TryGetValue(data.Client, out var infernoInfo) && (DateTime.Now - infernoInfo.Time).TotalSeconds < 0.2)
                            {
                                string grenadeName = infernoInfo.IsIncendiary ? "incendiary" : "molotov";
                                PrintToPlayerChat(player, Localizer[$"matchzy.pracc.{grenadeName}", player.PlayerName, $"{(DateTime.Now - data.Time).TotalSeconds:0.00}"]);
                            }
                        });
                    }
                    lastGrenadeThrownTime.Remove((int)entity.Index);
                }
            }
        }
        catch (Exception e)
        {
            Log($"[OnEntityDeletedHandler FATAL] An error occurred: {e.Message}");
        }
    }

    public HookResult EventDecoyDetonateHandler(EventDecoyStarted @event, GameEventInfo info)
    {
        if (!isPractice || isDryRun) return HookResult.Continue;
        CCSPlayerController? player = @event.Userid;
        if (!IsPlayerValid(player)) return HookResult.Continue;
        if(lastGrenadeThrownTime.TryGetValue(@event.Entityid, out var data)) 
        {
            PrintToPlayerChat(player!, Localizer["matchzy.pracc.decoy", player!.PlayerName, $"{(DateTime.Now - data.Time).TotalSeconds:0.00}"]);
            lastGrenadeThrownTime.Remove(@event.Entityid);
        }
        return HookResult.Continue;
    }
}
