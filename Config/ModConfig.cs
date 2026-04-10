using GenericModConfigMenu;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace FestivalNudge.Config;

public sealed class ModConfig
{
    public bool NotifyMovements { get; set; } = true;
    public bool SkipWalkingNpcs { get; set; } = true;
    public KeybindList MoveNpcKey { get; set; } = new(SButton.LeftControl);
    public KeybindList PrecisionModKey { get; set; } = new(SButton.LeftShift);

    public KeybindList ResetNudgeKey { get; set; } = new(SButton.None);
    public KeybindList ResetAllNudgesKey { get; set; }  = new(SButton.None);

    public ModConfig()
    {
        Init();
    }

    private void Init()
    {
        NotifyMovements = true;
        SkipWalkingNpcs = true;
    }

    public void SetupConfig(IGenericModConfigMenuApi configMenu, IManifest ModManifest, IModHelper Helper)
    {
        configMenu.Register(
            mod: ModManifest,
            reset: Init,
            save: () => Helper.WriteConfig(this)
        );

        configMenu.AddBoolOption(
            mod: ModManifest,
            name: i18n.Config_NotifyMovementsName,
            tooltip: i18n.Config_NotifyMovementsDescription,
            getValue: () => NotifyMovements,
            setValue: value => NotifyMovements = value
        );
        
        configMenu.AddBoolOption(
            mod: ModManifest,
            name: i18n.Config_SkipWalkingNpcsName,
            tooltip: i18n.Config_SkipWalkingNpcsDescription,
            getValue: () => SkipWalkingNpcs,
            setValue: value => SkipWalkingNpcs = value
        );
        
        configMenu.AddKeybindList(
            mod: ModManifest,
            name: i18n.Config_MoveNpcKeyName,
            tooltip: i18n.Config_MoveNpcKeyDescription,
            getValue: () => MoveNpcKey,
            setValue: value => MoveNpcKey = value
        );
        
        configMenu.AddKeybindList(
            mod: ModManifest,
            name: i18n.Config_PrecisionModKeyName,
            tooltip: i18n.Config_PrecisionModKeyDescription,
            getValue: () => PrecisionModKey,
            setValue: value => PrecisionModKey = value
        );
        
        configMenu.AddKeybindList(
            mod: ModManifest,
            name: i18n.Config_ResetNudgeKeyName,
            tooltip: i18n.Config_ResetNudgeKeyDescription,
            getValue: () => ResetNudgeKey,
            setValue: value => ResetNudgeKey = value
        );
        
        configMenu.AddKeybindList(
            mod: ModManifest,
            name: i18n.Config_ResetAllNudgesKeyName,
            tooltip: i18n.Config_ResetAllNudgesKeyDescription,
            getValue: () => ResetAllNudgesKey,
            setValue: value => ResetAllNudgesKey = value
        );
    }
}