using BAModAPI;
using Capisoft.Lib.BaComputerGames;

[assembly: RegisterModClass(typeof(MCG_Doom.DoomMod))]

namespace MCG_Doom
{
    [ModEntryOnCityLoad]
    public sealed class DoomMod : ComputerGameMod<DoomGame>
    {
        protected override ComputerGameDefinition Definition =>
            ComputerGameDefinition
                .Create<DoomGame>(
                    "dudeldups:doom",
                    "DOOM",
                    "The classic DOOM shareware episode, running on your Big Ambitions computer.",
                    version: "0.2.0",
                    descriptionKey: "mcg_doom_description",
                    ruleset: "doom-shareware-v1")
                .WithNativeRetroEffects(false);
    }
}
