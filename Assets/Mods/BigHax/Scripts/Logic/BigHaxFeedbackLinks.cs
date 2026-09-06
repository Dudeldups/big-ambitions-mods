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
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAABmJLR0QA/wD/AP+gvaeTAAAFAElEQVR4nO2aW4hVVRjHf2u8pVNaeU1TUZtq0NQYDSMlqF4KNNQiwhIsEqzMqIigwpLewgeLLhJBSCYEFlH0UD1UJhVlqZVlESWKV4y8j+j462Gf05w5nsveZ585J6fzg3mYvb/z//7r22vvtddaGxo0aNCgQYMG/1dCtQXVsUAbMA14OYSwK6XefGAksAnYHEI4nt5lFVGHqQvUN9TdduXFlNrN6r4cvVPql+oKdZbau1rtSGrsInWh+n7GVDHa1VEp8jxWQlv1L3WNOrvSYiS6BdSZwAPAfKBPzJ+9BXwMjAFGA8OAAUAz0DcTcwQ4DRwE9gI7gN3AKmB4zDy7gFeB10II+2P+Jh7qQPXbMlfjv0K7+lTctsXqAeoLwNKKK1h7zgDXhRC+KhdYtgBqG/A10KsKxmrJVmBaCOFUqaCmUifVJuAlzr3GA0wGHioXVLIHqA8CqYayOnMMaAkh7CkWULQHqM3A093hqoY0A0+WCih1CywlGrLOdRar44udLFgA9XzgkW6zVFv6AMuLnSzWA5YCQ7vFTn1YoLYUOnFWAdTz6DlXP0sv4P5CJwr1gHnAkG61Ux8WZW7tLhQqwH01MFMPBgF35R/s8h6QuU+25x/vQWwJIUzNPZDfA+6l5zYeYIp6ee6B/ALMq6GZetGljf8WIFOZgkNFD2Nu7j+5PeCWlMKHgBXADGACMAtYCaRZw9sOLAGuAq4AZgPr09lkujr6rKPqRykWIbZaZOlLbVF/r0Bzjdq3iOY89UQKvwvzBfukEDxooYp21W9VjyXQ/FwtOQVXF1foV/WVfLEpKcSeKGU0J8fzCTTbYugFdUuFnjdndbLPgGlxGlGEtVWO2xZC2FQuKIQgsC6mZj6T1IHQWYCpJYJLsT+EsDNm7FagI0bcdwnyf58gNp9xAE1GT9skSXNpBqbHjL0hZtxM4+/yxNUsRFQAomWvNHtsy2LGxd1XGAwsKBekDgXujKlZiHFZoWkVDiVZzqi3lTG7JKHmAaNd5mJ6Teq7KX1vy4rdmFJI9aRRI5vyjPZWH1c7KtDcoV5boPGD1fVV8PwHQFBvBj5M3IEK8yvwAdEG5yhgDtmuVhkCnwIbiEaQVuBWosWNtOwLIYwI6hzgvSoInmscDiEMaiL+NndPoz9Eo0BPXgEqxUmICnCkzkbqxRGICnCozkbqxWFIX4C/gZ+qYic5HUTfLVTKUYgKcIBkE5Asx4HrQwiTiJa/3gTaUxiKy07gGWBsCGEGle9gd36+p05QN1TwMrFfXab2y+hcrN6trjV6m6sWPxgtqNxkZqVIHaO+rp5OqNWhrlT7Q84IYPQW9zDwHJkhIgF7gNXA6hDC3hy9tszfRKKXmFaijx6LcRL4k+i2+hn4Efgi92NL9RqiecXtQL+EPn8DFoUQNhaNMFrErPQ9e5sxpsfqBepI9Up1vDrETC8q87u2Cn0dVZ9VB8QulTpD3Zgw0dzyyulQ30ngp0N92xITq3LJgnqH8b4R/KzKbS3mqcVo8lWKU+o6dXI1E8/MVLPQQ6ddnVi1ZOW9LC/S8MPqqoqveMzkl2UM/JKT+NFuS1jYQ2/1m0zu0+on6j1mVnpraaTNaL4fa12wyrlbjYbgS2qdu0GDBg0aNGjQM/gH9nGR0t/T2vEAAAAASUVORK5CYII=";

        private static Sprite? steamIcon;
        private static Sprite? discordIcon;

        public static bool HasDiscordProfile => DiscordUserId.Length > 0;
        public static Sprite SteamIcon => steamIcon ??= CreateIcon("BigHax Steam Icon", SteamIconPng);
        public static Sprite DiscordIcon => discordIcon ??= CreateIcon("BigHax Discord Icon", DiscordIconPng);

        public static void OpenSteam() => Application.OpenURL(SteamWorkshopUrl);

        public static void OpenDiscord()
        {
            if (HasDiscordProfile)
                Application.OpenURL("https://discord.com/users/" + DiscordUserId);
        }

        private static Sprite CreateIcon(string name, string png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = name + " Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(png), markNonReadable: true))
                throw new InvalidOperationException("Could not decode " + name + ".");

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 64f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
