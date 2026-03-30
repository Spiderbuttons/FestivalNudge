using System;
using System.Collections.Generic;
using System.Linq;
using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using FestivalNudge.Config;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using FestivalNudge.Helpers;
using StardewValley.Extensions;
using StardewValley.Pathfinding;
using StardewValley.TokenizableStrings;
using xTile.Tiles;

namespace FestivalNudge
{
    internal sealed class ModEntry : Mod
    {
        internal static IModHelper ModHelper { get; set; } = null!;
        internal static IMonitor ModMonitor { get; set; } = null!;
        internal static ModConfig Config { get; set; } = null!;
        internal static Harmony Harmony { get; set; } = null!;

        public override void Entry(IModHelper helper)
        {
            i18n.Init(helper.Translation);
            ModHelper = helper;
            ModMonitor = Monitor;
            Config = helper.ReadConfig<ModConfig>();
            Harmony = new Harmony(ModManifest.UniqueID);

            Harmony.PatchAll();
            
            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null) Config.SetupConfig(configMenu, ModManifest, Helper);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (Game1.CurrentEvent is not { isFestival: true })
            {
                FestivalManager.TileAccessibility = null;
                FestivalManager.NpcAccessibility = null;
                FestivalManager.NudgedNpcs = null;
            }
        }
        
        // Source - https://stackoverflow.com/a/3875619
        // Posted by Michael Borgwardt, modified by community. See post 'Timeline' for change history
        // Retrieved 2026-03-22, License - CC BY-SA 4.0
        public static bool NearlyEqual(double a, double b, double epsilon)
        {
            const double MinNormal = 2.2250738585072014E-308d;
            double absA = Math.Abs(a);
            double absB = Math.Abs(b);
            double diff = Math.Abs(a - b);

            if (a.Equals(b))
            {
                return true;
            }

            if (a == 0 || b == 0 || absA + absB < MinNormal) 
            {
                return diff < (epsilon * MinNormal);
            }

            return diff / (absA + absB) < epsilon;
        }
    }

    [HarmonyPatch]
    public static class FestivalManager
    {
        public static bool?[,]? TileAccessibility;
        public static bool?[,]? NpcAccessibility;

        public static int? NudgedNpcs;

        public static bool alreadyManagedFestival = false;
        
        public static bool ShouldManageThisFestival => !alreadyManagedFestival && Game1.CurrentEvent is { isFestival: true } && NudgedNpcs is null or > 0;
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.getGeneralDirectionTowards))]
        public static void getGeneralDirectionTowards_Prefix(ref bool useTileCalculations)
        {
            if (ShouldManageThisFestival)
            {
                useTileCalculations = false;
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.PlayerControl))]
        public static void PlayerControl_Prefix(Event @event, string[] args, EventContext context)
        {
            GlobalFadeToClear_Prefix(@event, args, context);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.GlobalFadeToClear))]
        public static void GlobalFadeToClear_Prefix(Event @event, string[] args, EventContext context)
        {
            if (!Context.IsMainPlayer || !ShouldManageThisFestival) return;
            
            // Before you ask why I'm doing a bunch of Rounding and Dividing and Int Converting and Point Converting and bla bla bla...
            // I needed the pixel position, so I can't just use their TilePoint, but since pixel positions are floats, I was worried about
            // floating point math bugs. So all the stuff I'm doing is to try and ensure that every Vector2 position I use is correctly
            // compared to other floating points when necessary, since I'm using them as Dictionary keys.
            
            TileAccessibility = null;
            NpcAccessibility = null;
            NudgedNpcs = 0;
            FillInaccessibleTiles();
            HashSet<string> movingNpcs = @event.npcControllers?.Select(con => con?.puppet?.Name ?? "").ToHashSet() ?? [];
            
            Dictionary<Point, List<NPC>> occupiedTiles = new Dictionary<Point, List<NPC>>();
            foreach (var actor in @event.actors)
            {
                Point actorPos = new Point((int)Math.Round(actor.Position.X), (int)Math.Round(actor.Position.Y));
                if (!occupiedTiles.TryAdd(actorPos, [actor]))
                {
                    var originalPos = actorPos;
                    if (ModEntry.Config.SkipWalkingNpcs && movingNpcs.Contains(actor.Name))
                    {
                        Log.Trace($"{actor.Name} overlaps with {string.Join(", ", occupiedTiles[actorPos].Select(npc => npc.Name))} at tile {(actorPos.ToVector2() / 64f).ToPoint()}, but they're set up to move, so position adjustment will be skipped to be better safe than sorry.");
                        continue;
                    }
                    
                    Log.Trace($"{actor.Name} overlaps with {string.Join(", ", occupiedTiles[actorPos].Select(npc => npc.Name))} at tile {(actorPos.ToVector2() / 64f).ToPoint()}. Attempting to find a nearby free tile to move them to.");
                    var neighbours = GetAccessibleNeighbours(actorPos.ToVector2() / 64f);
                    
                    if (neighbours.Count > 0)
                    {
                        var newTile = neighbours[Game1.random.Next(neighbours.Count)];
                        actor.Position = newTile * 64f;
                        NpcAccessibility?[(int)newTile.X, (int)newTile.Y] = false;
                        actorPos = new Point((int)Math.Round(actor.Position.X), (int)Math.Round(actor.Position.Y));
                    }
                    else
                    {
                        var neighboursWithNpcs = GetAccessibleNeighbours(actorPos.ToVector2() / 64f, includeNpcCheck: false);
                        if (neighboursWithNpcs.Count > 0)
                        {
                            var offsets = GetOffsetsFromTile(actorPos.ToVector2() / 64f, neighboursWithNpcs).ToList();
                            var chosenOffset = offsets[Game1.random.Next(offsets.Count)] / 2f; // We only wanna nudge em by half a tile.
                            
                            if (ModEntry.NearlyEqual(0, chosenOffset.X, 0.0001f))
                                chosenOffset += new Vector2(Game1.random.NextBool() ? 0.5f : -0.5f, 0); // If there's no X movement, it can be difficult to see an NPC behind another.
                            if (ModEntry.NearlyEqual(0, chosenOffset.Y, 0.0001f))
                                chosenOffset += new Vector2(0, Game1.random.NextBool() ? 0.5f : -0.5f); // If there's no Y movement, then the person in front just looks rude lol
                            
                            // I know that means I might as well be removing all the non-corner offsets, but I didn't feel like writing another function for it.
                            // And if I did Where(x/y != 0) before that ToList() up there then we may end up with a completely empty offset list.
                            // Neither of these things are insurmountable problems, I'm just lazy.
                            
                            actor.Position += chosenOffset * 64f;
                            actor.Position += new Vector2(0, 1f); // This is to prevent z-fighting.
                            actorPos = new Point((int)Math.Round(actor.Position.X), (int)Math.Round(actor.Position.Y));
                            Log.Trace($"No completely free tiles found for {actor.Name}, but they can scoot a little closer to another NPC instead.");
                        }
                        else
                        {
                            Log.Trace($"Unable to move {actor.Name} because there is no space available.");
                            occupiedTiles[actorPos].Add(actor);
                            continue;
                        }
                    }
                    
                    occupiedTiles[actorPos] = [actor];
                    NudgedNpcs++;
                    
                    string logMsg = $"Moved {TokenParser.ParseText(actor.GetTokenizedDisplayName())} to tile {(actor.Position / 64f).ToPoint()} to prevent overlap with {string.Join(", ", occupiedTiles[originalPos].Select(npc => TokenParser.ParseText(npc.GetTokenizedDisplayName())))}.";
                    if (ModEntry.Config.NotifyMovements) Log.Info(logMsg);
                    else Log.Trace(logMsg);
                }
            }

            alreadyManagedFestival = true;
        }

        private static List<Vector2> GetAccessibleNeighbours(Vector2 centerTile, bool includeNpcCheck = true)
        {
            List<Vector2> neighbours = [];
            if (TileAccessibility == null || NpcAccessibility == null) return neighbours;

            int x = (int)centerTile.X;
            int y = (int)centerTile.Y;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int newX = x + dx;
                    int newY = y + dy;

                    if (newX >= 0 && newX < TileAccessibility.GetLength(0) && newY >= 0 && newY < TileAccessibility.GetLength(1))
                    {
                        if (TileAccessibility[newX, newY] is null or true && (!includeNpcCheck || NpcAccessibility[newX, newY] is null or true))
                        {
                            neighbours.Add(new Vector2(newX, newY));
                        }
                    }
                }
            }

            return neighbours;
        }

        private static IEnumerable<Vector2> GetOffsetsFromTile(Vector2 referenceTile, IEnumerable<Vector2> tiles)
        {
            foreach (var tile in tiles)
            {
                var distanceFromReference = tile - referenceTile;
                yield return distanceFromReference;
            }
        }

        private static bool WouldTileBlockView(this GameLocation loc, Vector2 tileLocation)
        {
            Tile? frontTile = loc.Map.GetLayer("Front")?.Tiles[(int)tileLocation.X, (int)tileLocation.Y];
            if (frontTile != null)
            {
                return true;
            }
            
            Tile? alwaysFrontTile = loc.Map.GetLayer("AlwaysFront")?.Tiles[(int)tileLocation.X, (int)tileLocation.Y];
            if (alwaysFrontTile != null)
            {
                return true;
            }
            
            return false;
        }

        private static void FillInaccessibleTiles()
        {
            if (!Game1.eventUp) return;

            var mapWidth = Game1.currentLocation.Map.DisplayWidth / 64;
            var mapHeight = Game1.currentLocation.Map.DisplayHeight / 64;
            
            TileAccessibility = new bool?[mapWidth, mapHeight];
            NpcAccessibility = new bool?[mapWidth, mapHeight];

            for (int x = 0; x < mapWidth; x++)
            {
                for (int y = 0; y < mapHeight; y++)
                {
                    Vector2 tile = new Vector2(x, y);
                    TileAccessibility[x, y] = !Game1.currentLocation.IsTileBlockedBy(tile, collisionMask: CollisionMask.All) // The Characters mask doesn't even check Event.actors
                                              && Game1.currentLocation.isTilePassable(tile)
                                              && !Game1.currentLocation.WouldTileBlockView(tile - new Vector2(0, 1f)); // Check to see if the NPC's head would be covered.
                }
            }

            foreach (var actor in Game1.CurrentEvent.actors)
            {
                NpcAccessibility[actor.TilePoint.X, actor.TilePoint.Y] = false;
                switch (actor.FacingDirection)
                {
                    case 0:
                        int newY = actor.TilePoint.Y - 1;
                        if (newY >= 0) NpcAccessibility[actor.TilePoint.X, newY] = false;
                        break;
                    case 1:
                        int newX = actor.TilePoint.X + 1;
                        if (newX < NpcAccessibility.GetLength(0)) NpcAccessibility[newX, actor.TilePoint.Y] = false;
                        break;
                    case 2:
                        newY = actor.TilePoint.Y + 1;
                        if (newY < NpcAccessibility.GetLength(1)) NpcAccessibility[actor.TilePoint.X, newY] = false;
                        break;
                    case 3:
                        newX = actor.TilePoint.X - 1;
                        if (newX >= 0) NpcAccessibility[newX, actor.TilePoint.Y] = false;
                        break;
                }

                if (actor.Name.Equals("Lewis"))
                {
                    // This'll ensure that the player can always navigate to Lewis no matter what, in order to start any (vanilla) festival that requires him.
                    var lewisPoint =  actor.TilePoint;
                    var playerPoint = Game1.player.TilePoint;

                    Stack<Point>? pathToLewis = PathFindController.findPath(lewisPoint, playerPoint, PathFindController.isAtEndPoint, Game1.currentLocation, Game1.player, 50000);
                    while (pathToLewis?.Count > 0)
                    {
                        var point = pathToLewis.Pop();
                        TileAccessibility[point.X, point.Y] = false;
                        NpcAccessibility[point.X, point.Y] = false;
                    }
                }
            }
        }
    }
}