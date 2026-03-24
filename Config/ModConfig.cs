using GenericModConfigMenu;
using StardewModdingAPI;

namespace FestivalNudge.Config;

public sealed class ModConfig
{
    public bool NotifyMovements { get; set; } = true;
    public bool SkipWalkingNpcs { get; set; } = true;

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
    }
}