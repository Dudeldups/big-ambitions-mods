#nullable enable
using System;
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxFeedbackLinks
    {
        public const string SteamWorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3744259108";

        private const string DiscordUserId = "576848055882612754";

        private const string SteamIconPng =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAABmJLR0QA/wD/AP+gvaeTAAAE10lEQVR4nO2abWiWVRjH/9d0reVyZYFo5htLCymlXBZBWRB9qUyMWiiUFVghEX4yqCiMIsKiMuyN6AWiISSR2JfIdEojwSx61TJti1azpjNnW9t+fThn7Onsfl7u52X3pPsH4zw7b/f1P+c5L9d1P1JKSkpKSkpKSkrK/xJL2oBsACZpjv+rl9Qn6XdJe83sSJK2VRRgDvAC0EE0A8CnwCrg1KTtLRtAHbAB6M8iPIqfgRuTtr1kgFnANzGEhzyNWzKxSXwPAKZL2inp3IjiY5J2S+qUNF5Sg6QLJVVF1N1oZvdVys6KANQAeyJm9AdgOVAT0Waan/HeiHYn3QA8GiGiGTjNl1cDVwF3+QGZm9H2YqA9aHvcf6PGPsBZwF+BgM3AOF9+J/BrxABtB+b5OnOBrqD8tWSVFQiwJjD8N+BMX/ZUhPBMuoHLfd07grITwBnJqisAoCUwfK3PvyGP+CHacEdnFbAvKLstaX05Acb7mcpkpi9rLXAAAFb7NuuC/OcLtSXqOBkNpkjKvMUdNrODuCVwaYx+rvPp7iB/dqEdJDUA4Ro97NPJinc3merTziB/YqEdJDUADwb/1/k0rpPT5dPTg/y/C+1g1AcAuFVSuEmdA0wysw5J+2N0t8OnFwX5h4q1r6IAU4E/smxoK32dewvcAI8BU3ybnUHZqmSVRgAYsDWHoK9wp0MVsCWP+EFgue/3yqBsgLF4GwTuKWBW1/m6NbiYQJRr3AEs9fXqge+D8q1xDVsB1FZAc+YzGhh57c02s2sy2s0CHgBeBNYDTQz7CZMYeZkaABrjGjeIW5frgfPKrF3AOGBXAeIz2Qw0ZOnPgGXAoYh2z8W1zwAy+5f0kaSNkj4ws/7iZP/H4LWSniyi6aCkFrlYQbukGkkXyF1+ZkbUb5F0rZn1xjWwL8sstAGP4HfaYgDmE+23l5ttQH2xRobuZEgfsAm4hhhhJ9xG9mVFZTvbngCqixLvDT0Q44HfAveTx90Easnv0pZCD/AGcH7Rwj0GtEpaFLNdj6R35OJwe3Dh6RWSmiQ1KsZdvEC2yN3ufpG0V9IOMztelp6Bd0ucjVbgYJAXurrF0oW7OleMKknfldjHIkkz/Of9kq4ws1pJ1ZIukfSM3DcmLi2SFphZc4n25QZYUqbZGmIA+BAX2RmK7zXgrrqF8A/w0FDbigOc7Y2uBAeA6/1zJuOO1lz8CFw2KsKDQdhUoQEAN7g3++cszVHvTaDcm2esQZgPvIRzM8tNJzABd439KSg7wlgKYgITgdXA12UehGW+/9cz8lqAGflsqiQjIkJm1m1mG8xsnqTFkprl3s2XylCgst2nzZIWm1mi0ZucITEz225mTXLH3MOS2kp41p8+neDTz81soIT+Rh+ca7skYh3nox8fpQE+9nk3Ja2naBh+qXmU7DG+TJ717abjzvle/GuwkxIvpNfP7NXASuCzLOJfwXtrDB+3byWtoWQY9vba8ZEkYCHwOPCyTxdm1H/M1+9mLAYt4wKcAnziRXUBdxPhlwOzgfcy9oIxtfZL+okMUCfpbUlDojolbZNzXeskLZBzlqokHZV0u5m9X8ozxyTALcAXWfaAHuBVYFrSdkZR1h9J4X7C0ij39veEpH2SdpUteJGSkpKSkpKSkpKSUi7+BdT7CZKTUB4kAAAAAElFTkSuQmCC";

        private const string DiscordIconPng =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAUjSURBVHhe7ZppqFVVFMf3NRtsEGwwmqGJpMjABps+ZURQ0fiePCuyIiqMFCMMpTAiipJGEIlosEGigfBbBZFlA/mhwexZRINUVlQ2WPle57dinbff49y/dzjnviHv9fxg83j7rLX2Xuvus4d1dgglJSUlJSUlJSMEcAhwIXAncKA+LwpwEXADcIqZ7arP/3eAycAsM3sc+M4yAA+pfBGA3YAfMvb6gXeA24HTzWy86owJZjYJuBxY6Z3KOp0F+Ac4QPXzAtykNrMAvwBPAueOSTDM7DTgWaBPO1MP4GkzuwK4FXgEeBl4FXjbzNZ4AV6PdSuA+4F5QDewUe3VA9gALPQRqf0eNsDE2NltnjjqFqkPwwJ4UBvalgESYLr60RJmNg34VxvZ1gE+NLMd1Z9CmNk44F013i4A89WnQgBz1Gg7AfwJ7Kd+5ULX4HYFeFh9y4WZLVBj7Uhcsg9V/xpiZrsDP6qxdgV4Qn1sCHCLGmlnfBUDjlA/a2JmuwA/qZF2B7hPfa0J0KPKHcImf7XV363wfblqdgrAtepvFf6eAKhipwB8oD5XYWZ3qVKnARypfg8BfKYKHcgC9TvFI6OSnQjwnvqeAsxV4YJsAhYDJ/nOKyZP7gU2q2BegF6fuIBj/AcCzgGeV7ki+BwHHKT+ewBeUeG8AB/VS33FifUL1WmGp7rMbCe158Sk69+qkxdP51UZ9HNzqwaBn2tGNAMwpchIAFaZ2Q5qJ4uZXaN6eQGWVhkDpqpQAWpPKgJwjyo2YJrqK2ZWiUmPwmy1HAJXqVBemv36g5jZcapbC+AT1a1HqyfWeDaYOGTI8/cqlAfPF1T1qAExu9Q0tQYsV916AGepfh5iAKZmDc1WoTx4xiWEcEJVr+pgZjNyBuBL1a0HcLfq5yH24/xBI3OGGYCntGO1iAGYDlzvs7h/yFB7DvA7IcxSfQXYB/gGOLWZTSWOgHmDho5XgSLEdfVi7WAW4DrVa4Qfx9VGlvg6vaR6RQDWpcaAM/RhUYAt0clx0tHxwM2ep1edZgBfAydn7TnAXsALKl+UoVcNODut8QHUPbxCF+vpYkksK+jmL2ayb8vlUibTxTq6WBbLKuuxSXSzcNili6vDzLDIA3CeRmd7APhtcASkAYiTSk+SJI8lSfJtkiRkSlt9GsuDZ4sHApAkK2NFRwUgftCtC/DHYACeiRWjEoBmHRktmrXrlzkGAtDfvzRWbG8B6E0DYP39aRqslQAAvwJrtT5Ls460StzM7CH9HCqNnsWyZmAE9PXN9wNI3FU1DICLZzqwGTg2DeLA/8v9SK0N5ehI0eL9vC3NP8CZ3n6SJH6WqWTlcrQ7EIA0CHAY8GY2wnnwz2fAjcDO0c6ewGV+LabZbq4gH8cmZ8T/15vZwcCjPhJEthkenSXABP/phmrjLm5uCOGOEMKEKpXmfB9CWOalUqls9Ipoz8/1Xo4OIUyJpRFbQghfhRD8SPxpCGFtCOGtrABwov81s0tCCGngC/B5CGF2pVJZrQ+UlvbZvr+mUtmgxhQfnsD+ZnZUzB/uPTiKGuGJEm0zD/GewOJC9wzjyW21GmsEcIHaGWmAF7XdesQzyHNqIzcx7dQtdTXvCAJvZOVGi5hk3VLtajXxQqVf50sn6RHB09weTUKoNen41TR/z8cEXwXUaSfNJcADKj+iAIfHZag30/DwLiMVJB6z349t+37gNeBKlRt14hU6P+83nfhGmphq9yW4tUtQJSUlJSUlJSUl2zn/AWSnPq3kkWCQAAAAAElFTkSuQmCC";

        private static Sprite? steamIcon;
        private static Sprite? discordIcon;
        private static bool steamIconInitialized;
        private static bool discordIconInitialized;

        public static bool HasDiscordProfile => DiscordUserId.Length > 0;
        public static Sprite? SteamIcon
        {
            get
            {
                if (!steamIconInitialized)
                {
                    steamIconInitialized = true;
                    steamIcon = CreateIcon("BigHax Steam Icon", SteamIconPng);
                }

                return steamIcon;
            }
        }

        public static Sprite? DiscordIcon
        {
            get
            {
                if (!discordIconInitialized)
                {
                    discordIconInitialized = true;
                    discordIcon = CreateIcon("BigHax Discord Icon", DiscordIconPng);
                }

                return discordIcon;
            }
        }

        public static void OpenSteam() => Application.OpenURL(SteamWorkshopUrl);

        public static void OpenDiscord()
        {
            if (HasDiscordProfile)
                Application.OpenURL("https://discord.com/users/" + DiscordUserId);
        }

        private static Sprite? CreateIcon(string name, string png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = name + " Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(png), markNonReadable: true))
                {
                    UnityEngine.Object.Destroy(texture);
                    BigHaxLogger.UiDiagnostic("could not decode " + name + "; button will remain available without its icon.");
                    return null;
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Object.Destroy(texture);
                BigHaxLogger.UiDiagnostic("could not decode " + name + "; button will remain available without its icon: " + exception.GetBaseException().Message);
                return null;
            }

            BigHaxLogger.UiDiagnostic("decoded " + name + " as " + texture.width + "x" + texture.height + ".");

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 64f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
