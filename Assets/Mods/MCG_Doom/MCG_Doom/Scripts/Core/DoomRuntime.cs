using System;
using ManagedDoom;
using MCG_Doom.Input;
using MCG_Doom.Rendering;

namespace MCG_Doom.Core
{
    internal sealed class DoomRuntime : IDisposable
    {
        private const double TicDuration = 1.0 / 35.0;
        private const double MaxFrameDelta = 0.25;

        private readonly Doom _doom;
        private readonly UnityDoomInput _input;
        private readonly UnityDoomVideo _video;
        private double _accumulator;
        private bool _completed;

        public DoomRuntime(string wadPath, DoomFrameBuffer frameBuffer)
        {
            var args = new CommandLineArgs(new[]
            {
                "-iwad", wadPath,
                "-nosound",
                "-nomusic"
            });

            var config = new Config
            {
                video_highresolution = false,
                video_fpsscale = 1,
                mouse_disableyaxis = true
            };

            var content = new GameContent(args);
            _input = new UnityDoomInput(config);
            _video = new UnityDoomVideo(config, content, frameBuffer);
            _doom = new Doom(args, config, content, _video, null, null, _input);

            _video.Render(_doom, Fixed.One);
        }

        public void Tick(double deltaSeconds)
        {
            if (_completed)
            {
                return;
            }

            _input.PumpEvents(_doom);
            _accumulator += Math.Max(0.0, Math.Min(deltaSeconds, MaxFrameDelta));

            while (_accumulator >= TicDuration)
            {
                var result = _doom.Update();
                _accumulator -= TicDuration;

                if (result == UpdateResult.Completed)
                {
                    _completed = true;
                    return;
                }
            }

            // Rendering does not advance game state. Fixed.One is a safe first
            // implementation; interpolation can be added after the in-game
            // integration has been verified.
            _video.Render(_doom, Fixed.One);
        }

        public void Dispose()
        {
            _input.Reset();
            _video.Dispose();
        }
    }
}
