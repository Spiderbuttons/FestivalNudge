using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GenericModConfigMenu;
using HarmonyLib;
using Microsoft.Xna.Framework;
using FestivalNudge.Config;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using FestivalNudge.Helpers;
using FestivalNudge.Models;
using Microsoft.Xna.Framework.Graphics;
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
        internal static IManifest Manifest { get; set; } = null!;
        internal static ModConfig Config { get; set; } = null!;
        internal static Harmony Harmony { get; set; } = null!;

        public override void Entry(IModHelper helper)
        {
            i18n.Init(helper.Translation);
            ModHelper = helper;
            Manifest = ModManifest;
            ModMonitor = Monitor;
            Config = helper.ReadConfig<ModConfig>();
            Harmony = new Harmony(ModManifest.UniqueID);

            Harmony.PatchAll();
            
            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            Helper.Events.Content.AssetRequested += OnAssetRequested;
            Helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;
            Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            
            Helper.Events.Input.ButtonsChanged += FestivalManager.OnButtonsChanged;
            Helper.Events.Input.MouseWheelScrolled += FestivalManager.OnMouseWheelScrolled;
            Helper.Events.GameLoop.UpdateTicked += FestivalManager.OnUpdateTicked;
            Helper.Events.Display.RenderedWorld += FestivalManager.OnRenderedWorld;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu != null) Config.SetupConfig(configMenu, ModManifest, Helper);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            FestivalManager.ReloadNudges();
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo($"{Manifest.UniqueID}/NudgeData"))
            {
                e.LoadFrom(() => new Dictionary<string, Dictionary<string, List<NpcNudgeData>>>(), AssetLoadPriority.Exclusive);
            }

            if (e.NameWithoutLocale.IsEquivalentTo($"{Manifest.UniqueID}/NudgeCursor"))
            {
                e.LoadFromModFile<Texture2D>(Path.Combine("assets", "cursor.png"), AssetLoadPriority.Medium);
            }
        }

        private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
        {
            if (e.NamesWithoutLocale.Any(asset => asset.IsEquivalentTo($"{Manifest.UniqueID}/NudgeData")))
            {
                FestivalManager.NudgeData = null!;
            }
            
            if (e.NamesWithoutLocale.Any(asset => asset.IsEquivalentTo($"{Manifest.UniqueID}/NudgeCursor")))
            {
                FestivalManager.NudgeCursor = null!;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (Game1.CurrentEvent is not { isFestival: true })
            {
                FestivalManager.ResetFestivalManagement();
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
        public class ManualNudge(NPC npc, Vector2 startingPos, int startingFacingDir)
        {
            public NPC Npc = npc;
            
            public Vector2 StartPos = startingPos;
            public int StartFacingDir = startingFacingDir;
            
            public Vector2 NewPos = startingPos;
            public int NewFacingDir = startingFacingDir;

            public bool isPrecise;
            public bool isValidTile = true;

            private readonly AnimatedSprite NpcSprite = npc.Sprite.Clone();

            public void Update()
            {
                NewPos = Game1.getMousePosition().ToVector2() + new Vector2(Game1.viewport.X, Game1.viewport.Y) - new Vector2(32, 32);
                NpcSprite.faceDirection(NewFacingDir);
                if (Npc.Sprite.CurrentAnimation is { } list && list.Any()) NpcSprite.CurrentFrame = Npc.Sprite.CurrentFrame;
                NPC? npc = Game1.currentLocation?.isCharacterAtTile(GetFinalPosition(forceTile: true));
                isValidTile = npc == null || npc == Npc;
            }

            public Vector2 GetFinalPosition(bool forceTile = false)
            {
                if (isPrecise && !forceTile) return NewPos;
                return new Vector2((int)Math.Round(NewPos.X / 64f), (int)Math.Round(NewPos.Y / 64f)) * 64f;
            }

            public void Draw(SpriteBatch b)
            {
                DrawPlacementTile(b);
                DrawNpc(b);
            }

            private void DrawPlacementTile(SpriteBatch b)
            {
                b.Draw(
                    texture: Game1.mouseCursors,
                    position: Game1.GlobalToLocal(Game1.viewport, GetFinalPosition()),
                    sourceRectangle: new Rectangle(isValidTile ? 194 : 210, 388, 16, 16),
                    color: Color.White,
                    rotation: 0f,
                    origin: Vector2.Zero,
                    scale: 4f,
                    effects: SpriteEffects.None,
                    layerDepth: 0.01f
                );
            }

            private void DrawNpc(SpriteBatch b)
            {
                b.Draw(
                    texture: NpcSprite.Texture,
                    position: Game1.GlobalToLocal(Game1.viewport, GetFinalPosition()),
                    sourceRectangle: NpcSprite.SourceRect,
                    color: Color.White * 0.5f,
                    rotation: Npc.rotation,
                    origin: new Vector2(0f, NpcSprite.SpriteHeight * 2.5f / 4f),
                    scale: Math.Max(0.2f, Npc.Scale) * 4f,
                    effects: Npc.flip || (NpcSprite.CurrentAnimation != null && NpcSprite.CurrentAnimation[NpcSprite.currentAnimationIndex].flip)
                        ? SpriteEffects.FlipHorizontally
                        : SpriteEffects.None,
                    layerDepth: 0.02f
                );
            }
        }

        public record SerializableNudge(Vector2 StartPos, Vector2 NewPos, int StartFacing, int NewFacing, bool Precise);

        public static Dictionary<string, SerializableNudge>? SavedNudges;
        public static Dictionary<string, SerializableNudge>? AutomaticNudges;
        
        public static Dictionary<string, Dictionary<string, List<NpcNudgeData>>> NudgeData
        {
            get => field ??= Game1.content.Load<Dictionary<string, Dictionary<string, List<NpcNudgeData>>>>($"{ModEntry.Manifest.UniqueID}/NudgeData");
            set;
        }

        public static Texture2D? NudgeCursor
        {
            get => field ??= Game1.content.Load<Texture2D>($"{ModEntry.Manifest.UniqueID}/NudgeCursor");
            set;
        }

        public static bool?[,]? TileAccessibility;
        public static bool?[,]? NpcAccessibility;

        public static int? NudgedNpcs;

        public static bool alreadyManagedFestival;
        
        public static bool ShouldManageThisFestival => !alreadyManagedFestival && Game1.CurrentEvent is { isFestival: true } && NudgedNpcs is null or > 0;
        public static string FestivalId => (Game1.CurrentEvent?.id ?? "").StartsWithIgnoreCase("festival_") && (Game1.CurrentEvent?.id ?? "").Length > 9 ? (Game1.CurrentEvent?.id ?? "")[9..] : Game1.CurrentEvent?.id ?? "";

        public static ManualNudge? ManuallyNudgedNpc;

        public static void ResetFestivalManagement()
        {
            TileAccessibility = null;
            NpcAccessibility = null;
            NudgedNpcs = null;
            alreadyManagedFestival = false;
            ManuallyNudgedNpc = null;
            AutomaticNudges = null;
        }

        public static void ReloadNudges()
        {
            if (Game1.CurrentEvent?.isFestival ?? false)
            {
                ResetAllNudgesInCurrentFestival(saveNudges: false);
                ResetFestivalManagement();
            }
            
            if (ModEntry.Config.PerSaveNudges)
            {
                SavedNudges = ModEntry.ModHelper.Data.ReadSaveData<Dictionary<string, SerializableNudge>>("manual-nudges") ?? new Dictionary<string, SerializableNudge>();
            }
            else
            {
                SavedNudges = ModEntry.ModHelper.Data.ReadJsonFile<Dictionary<string, SerializableNudge>>("manual-nudges.json") ?? new Dictionary<string, SerializableNudge>();
            }
            
            if (Game1.CurrentEvent?.isFestival ?? false) FixOverlaps(Game1.CurrentEvent);
        }

        public static void SaveNudges()
        {
            if (ModEntry.Config.PerSaveNudges) ModEntry.ModHelper.Data.WriteSaveData("manual-nudges", SavedNudges);
            else ModEntry.ModHelper.Data.WriteJsonFile("manual-nudges.json", SavedNudges);
        }

        public static string GetNudgeKeyForNpc(string npc, string festivalId, int year)
        {
            if (Game1.CurrentEvent is not { isFestival: true } @event || !@event.TryGetFestivalDataForYear($"set-up", out _, out string actualKey))
            {
                // This should never happen anyway.
                return npc;
            }
            
            return $"{npc}_{festivalId}_{actualKey}";
        }

        public static void SaveManualNudge(ManualNudge nudge, string festivalId)
        {
            string nudgeKey = GetNudgeKeyForNpc(nudge.Npc.Name, festivalId, Game1.year);
            Vector2 startPos = SavedNudges!.GetValueOrDefault(nudgeKey)?.StartPos ?? nudge.StartPos;
            int startFacing = SavedNudges!.GetValueOrDefault(nudgeKey)?.StartFacing ?? nudge.StartFacingDir;
            SerializableNudge savedNudge = new SerializableNudge(startPos, nudge.NewPos, startFacing, nudge.NewFacingDir, nudge.isPrecise);
            SavedNudges![nudgeKey] = savedNudge;
            SaveNudges();
        }

        public static void ResetNudge(NPC npc, string festivalId, bool isManual = true, bool saveNudges = true)
        {
            Dictionary<string, SerializableNudge>? nudgeDict = isManual ? SavedNudges : AutomaticNudges;
            
            string nudgeKey = GetNudgeKeyForNpc(npc.Name, festivalId, Game1.year);
            if (nudgeDict is null || !nudgeDict.TryGetValue(nudgeKey, out var nudge)) return;
            
            npc.Position = nudge.StartPos;
            npc.faceDirection(nudge.StartFacing);
            nudgeDict.Remove(nudgeKey);
            
            if (isManual && saveNudges) SaveNudges();
        }

        public static void ResetAllNudgesInCurrentFestival(bool includeAutomaticNudges = false, bool saveNudges = true)
        {
            if (Game1.CurrentEvent is not { isFestival: true }) return;
            
            foreach (var actor in Game1.CurrentEvent.actors)
            {
                ResetNudge(actor, FestivalId, isManual: true, saveNudges);
                if (includeAutomaticNudges) ResetNudge(actor, FestivalId, isManual: false, saveNudges);
            }
        }

        public static void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (Game1.CurrentEvent is not { isFestival: true }) return;
            
            if (ModEntry.Config.MoveNpcKey.GetKeybindCurrentlyDown() is { } bind && bind.GetState() == SButtonState.Pressed)
            {
                NPC? npc = Game1.currentLocation.isCharacterAtTile(e.Cursor.Tile) ??
                           Game1.currentLocation.isCharacterAtTile(e.Cursor.Tile + new Vector2(0, 1));
                if (ManuallyNudgedNpc is null && npc is not null)
                {
                    if (Game1.CurrentEvent.npcControllers is null || Game1.CurrentEvent.npcControllers.All(c => c.puppet != npc))
                    {
                        ManuallyNudgedNpc = new ManualNudge(npc, npc.Position, npc.FacingDirection);
                        Game1.playSound("button_tap");
                    }
                    else
                    {
                        Game1.addHUDMessage(new HUDMessage(i18n.Error_CannotMove(), HUDMessage.error_type));
                        Game1.playSound("cancel");
                    }
                    return;
                }
            }

            if (ModEntry.Config.ResetNudgeKey.JustPressed())
            {
                bool withMod = ManuallyNudgedNpc is null && ModEntry.Config.ResetAllNudgesKey.IsDown();
                if (ManuallyNudgedNpc is not null)
                {
                    ResetNudge(ManuallyNudgedNpc.Npc, FestivalId);
                    ManuallyNudgedNpc = null;
                } else ResetAllNudgesInCurrentFestival(withMod);
            }
            
            if (ManuallyNudgedNpc is null) return;

            ManuallyNudgedNpc.isPrecise = ModEntry.Config.PrecisionModKey.IsDown();

            if (e.Pressed.Contains(SButton.MouseLeft))
            {
                SuppressNudgeKeybinds();
                if (!ManuallyNudgedNpc.isValidTile) return;

                ManuallyNudgedNpc.Npc.Position = ManuallyNudgedNpc.GetFinalPosition();
                ManuallyNudgedNpc.Npc.faceDirection(ManuallyNudgedNpc.NewFacingDir);
                SaveManualNudge(ManuallyNudgedNpc, FestivalId);
                ManuallyNudgedNpc = null;
                Game1.playSound("coin");
            } else if (e.Pressed.Contains(SButton.MouseRight))
            {
                SuppressNudgeKeybinds();
                ManuallyNudgedNpc = null;
                Game1.playSound("breathout");
            }
        }

        private static void SuppressNudgeKeybinds()
        {
            foreach (var button in ModEntry.Config.MoveNpcKey.GetKeybindCurrentlyDown()?.Buttons ?? [])
            {
                ModEntry.ModHelper.Input.Suppress(button);
            }
            ModEntry.ModHelper.Input.Suppress(SButton.MouseLeft);
            ModEntry.ModHelper.Input.Suppress(SButton.MouseRight);
        }

        public static void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (ManuallyNudgedNpc is null) return;
            ManuallyNudgedNpc.NewFacingDir = e.Delta switch
            {
                > 0 => (ManuallyNudgedNpc.NewFacingDir + 3) % 4,
                < 0 => (ManuallyNudgedNpc.NewFacingDir + 1) % 4,
                _ => ManuallyNudgedNpc.NewFacingDir
            };
            Game1.playSound("shwip");
        }

        public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            ManuallyNudgedNpc?.Update();
        }

        public static void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            ManuallyNudgedNpc?.Draw(e.SpriteBatch);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game1), nameof(Game1.drawMouseCursor))]
        public static void drawMouseCursor_Prefix()
        {
            if (ManuallyNudgedNpc == null) return;
            
            Game1.mouseCursorTransparency = 0f;
            Game1.spriteBatch.Draw(
                texture: NudgeCursor,
                position: new Vector2(Game1.getMouseX(), Game1.getMouseY()),
                sourceRectangle: new Rectangle(0,0,12,16),
                color: Color.White,
                rotation: 0f,
                origin: Vector2.Zero,
                scale: 3f,// + Game1.dialogueButtonScale / 150f,
                effects: SpriteEffects.None,
                layerDepth: 1f);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(NPC), nameof(NPC.draw), typeof(SpriteBatch), typeof(float))]
        public static void draw_Prefix(NPC __instance, ref float alpha)
        {
            //if (ManuallyNudgedNpc?.Npc == __instance) alpha /= 2f;
        }
        
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Character), nameof(Character.getGeneralDirectionTowards))]
        public static void getGeneralDirectionTowards_Prefix(ref bool useTileCalculations)
        {
            if (ShouldManageThisFestival)
            {
                useTileCalculations = false;
            }
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.LoadActors))]
        public static void LoadActors_Postfix(Event @event, string[] args, EventContext context)
        {
            // LoadActors happens in both the festival setup and the "Main Event" of the festival. We'll need to fix our overlaps again in the latter case.
            if (ArgUtility.TryGet(args, 1, out var layerId, out _, allowBlank: true, "string layerId") &&
                (layerId.EqualsIgnoreCase("MainEvent") || layerId.EqualsIgnoreCase("Main-Event")))
            {
                ResetFestivalManagement();
                FixOverlaps(@event, isMainEvent: true);
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.PlayerControl))]
        public static void PlayerControl_Prefix(Event @event, string[] args, EventContext context)
        {
            FixOverlaps(@event);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Event.DefaultCommands), nameof(Event.DefaultCommands.GlobalFadeToClear))]
        public static void GlobalFadeToClear_Prefix(Event @event, string[] args, EventContext context)
        {
            FixOverlaps(@event);
        }

        private static Point PositionToPoint(Vector2 pos)
        {
            // Before you ask why I'm doing a bunch of Rounding and Dividing and Int Converting and Point Converting and bla bla bla...
            // I needed the pixel position, so I can't just use their TilePoint, but since pixel positions are floats, I was worried about
            // floating point math bugs. So all the stuff I'm doing is to try and ensure that every Vector2 position I use is correctly
            // compared to other floating points when necessary, since I'm using them as Dictionary keys.
            decimal xPos = (decimal)pos.X;
            decimal yPos = (decimal)pos.Y;
            int xTile = (int)Math.Round(xPos / 64m);
            int yTile = (int)Math.Round(yPos / 64m);
            return new Point(xTile, yTile);
        }

        private static void FixOverlaps(Event @event, bool isMainEvent = false)
        {
            if (!Context.IsMainPlayer || !ShouldManageThisFestival) return;
            
            TileAccessibility = null;
            NpcAccessibility = null;
            NudgedNpcs = 0;
            FillInaccessibleTiles();
            HashSet<string> movingNpcs = @event.npcControllers?.Select(con => con?.puppet?.Name ?? "").ToHashSet() ?? [];
            
            Dictionary<Point, List<NPC>> occupiedTiles = new Dictionary<Point, List<NPC>>();
            foreach (var actor in @event.actors)
            {
                Point actorPos = PositionToPoint(actor.Position);
                
                string nudgeKey = GetNudgeKeyForNpc(actor.Name, FestivalId, Game1.year);
                if (!isMainEvent && SavedNudges!.TryGetValue(nudgeKey, out var savedNudge))
                {
                    actor.Position = savedNudge.Precise ? savedNudge.NewPos : PositionToPoint(savedNudge.NewPos).ToVector2() * 64f;
                    actor.faceDirection(savedNudge.NewFacing);
                    NpcAccessibility![PositionToPoint(savedNudge.NewPos).X, PositionToPoint(savedNudge.NewPos).Y] = false;
                    actorPos = PositionToPoint(actor.Position);

                    if (!occupiedTiles.TryGetValue(actorPos, out _))
                    {
                        occupiedTiles[actorPos] = [];
                    }
                    occupiedTiles[actorPos].Add(actor);
                    NudgedNpcs++;
                    
                    string logMsg = $"Loaded manual nudge data to move {TokenParser.ParseText(actor.GetTokenizedDisplayName())} to tile {PositionToPoint(actor.Position)}.";
                    if (ModEntry.Config.NotifyMovements) Log.Info(logMsg);
                    else Log.Trace(logMsg);
                    
                    continue;
                }
                
                if (!occupiedTiles.TryAdd(actorPos, [actor]))
                {
                    var originalPos = actorPos;
                    if (ModEntry.Config.SkipWalkingNpcs && movingNpcs.Contains(actor.Name))
                    {
                        Log.Trace($"{actor.Name} overlaps with {string.Join(", ", occupiedTiles[actorPos].Select(npc => npc.Name))} at tile {PositionToPoint(actor.Position)}, but they're set up to move, so position adjustment will be skipped to be better safe than sorry.");
                        continue;
                    }
                    
                    Log.Trace($"{actor.Name} overlaps with {string.Join(", ", occupiedTiles[actorPos].Select(npc => npc.Name))} at tile {PositionToPoint(actor.Position)}. Attempting to find a nearby free tile to move them to.");

                    if (NudgeData.TryGetValue(actor.Name, out var nudgeData) && nudgeData.TryGetValue(FestivalId, out var festivalData))
                    {
                        var newPosition = festivalData.FirstOrDefault(data =>
                            TileAccessibility![data.Position.X, data.Position.Y] is null or true &&
                            NpcAccessibility![data.Position.X, data.Position.Y] is null or true &&
                            (data.Condition is null || GameStateQuery.CheckConditions(data.Condition)));
                        if (newPosition is not null)
                        {
                            Log.Trace($"Moving {actor.Name} based on mod-authored nudge data.");
                            var newTile = newPosition.Position;
                            actor.Position = new Vector2(newTile.X, newTile.Y) * 64f;
                            NpcAccessibility![newTile.X, newTile.Y] = false;
                            actorPos = PositionToPoint(actor.Position);
                            goto finish;
                        }
                    }
                    
                    var neighbours = GetAccessibleNeighbours(PositionToPoint(actor.Position).ToVector2());
                    if (neighbours.Count > 0)
                    {
                        var newTile = neighbours[Game1.random.Next(neighbours.Count)];
                        actor.Position = newTile * 64f;
                        NpcAccessibility?[(int)newTile.X, (int)newTile.Y] = false;
                        actorPos = PositionToPoint(actor.Position);
                    }
                    else
                    {
                        var neighboursWithNpcs = GetAccessibleNeighbours(PositionToPoint(actor.Position).ToVector2(), includeNpcCheck: false);
                        if (neighboursWithNpcs.Count > 0)
                        {
                            var offsets = GetOffsetsFromTile(PositionToPoint(actor.Position).ToVector2(), neighboursWithNpcs).ToList();
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
                            actorPos = PositionToPoint(actor.Position);
                            Log.Trace($"No completely free tiles found for {actor.Name}, but they can scoot a little closer to another NPC instead.");
                        }
                        else
                        {
                            Log.Trace($"Unable to move {actor.Name} because there is no space available.");
                            occupiedTiles[actorPos].Add(actor);
                            continue;
                        }
                    }
                    
                    finish:
                    occupiedTiles[actorPos] = [actor];
                    AutomaticNudges ??= new Dictionary<string, SerializableNudge>();
                    AutomaticNudges[GetNudgeKeyForNpc(actor.Name, FestivalId, Game1.year)] = new SerializableNudge(originalPos.ToVector2() * 64f, actor.Position, actor.FacingDirection, actor.FacingDirection, false);
                    NudgedNpcs++;
                    
                    string logMsg = $"Moved {TokenParser.ParseText(actor.GetTokenizedDisplayName())} to tile {PositionToPoint(actor.Position)} to prevent overlap with {string.Join(", ", occupiedTiles[originalPos].Select(npc => TokenParser.ParseText(npc.GetTokenizedDisplayName())))}.";
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
                if (actor.TilePoint.X < 0 || actor.TilePoint.X >= NpcAccessibility.GetLength(0) || actor.TilePoint.Y < 0 || actor.TilePoint.Y >= NpcAccessibility.GetLength(1))
                {
                    Log.Trace($"{actor.Name} has an out-of-bounds TilePoint {actor.TilePoint}.");
                    continue;
                }
                
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