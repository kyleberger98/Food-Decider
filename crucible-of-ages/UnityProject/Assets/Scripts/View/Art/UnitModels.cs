using System;
using Crucible.View.Icons;

namespace Crucible.View.Art
{
    /// <summary>
    /// Low-poly miniatures (engine-free), one family per unit symbol. Foot troops come as a squad of
    /// three figures and cavalry as a pair of riders, Humankind style; machines, ships and aircraft
    /// are single models. Models stand on the origin facing +z, about a third of a hex across, and
    /// wear the owner's colour on tunics, sails, hulls or wing roundels.
    /// </summary>
    public static class UnitModels
    {
        static readonly Rgb Skin = Rgb.Hex(0xD9A77C), Cloth = Rgb.Hex(0x4A3F36), Steel = Rgb.Hex(0xB8BEC6), Iron = Rgb.Hex(0x4A4E55);
        static readonly Rgb Wood = Rgb.Hex(0x8A5E3A), WoodDark = Rgb.Hex(0x5E3E26), Horse = Rgb.Hex(0x7A4E2E), Canvas = Rgb.Hex(0xEDE6D2);
        static readonly Rgb Olive = Rgb.Hex(0x5C6B3A), Navy = Rgb.Hex(0x6B7784), Gold = Rgb.Hex(0xE3B94F), Plinth = Rgb.Hex(0x2A2A30);

        /// <summary>Figures are authored small and scaled up so they read at strategy-camera distance.</summary>
        public const float ModelScale = 1.6f;

        public static MeshData Build(UnitIcon icon, Rgb team, bool plinth = true)
        {
            var m = new MeshData();
            var o = Frame.At(0, 0, 0);
            bool naval = icon == UnitIcon.SailShip || icon == UnitIcon.Steamship || icon == UnitIcon.Carrier;
            bool air = icon == UnitIcon.Fighter || icon == UnitIcon.Bomber || icon == UnitIcon.Jet;
            if (plinth && !naval)
            {
                m.Frustum(o, 6, 0, 0.34f, 0.035f, 0.32f, Plinth, team * 0.85f, phase: (float)Math.PI / 6);
                o = o.Move(0, 0.035f, 0);
            }
            o = o.Scale(naval ? 1f : ModelScale); // ships are already hex-sized

            switch (icon)
            {
                case UnitIcon.Horse:
                    Rider(m, o.Move(-0.13f, 0, 0.02f), team);
                    Rider(m, o.Move(0.13f, 0, -0.06f), team);
                    break;
                case UnitIcon.Catapult: Catapult(m, o, team); break;
                case UnitIcon.Cannon: Cannon(m, o, team); break;
                case UnitIcon.Rocket: RocketTruck(m, o, team); break;
                case UnitIcon.Ram: Ram(m, o, team); break;
                case UnitIcon.SiegeTower: SiegeTower(m, o, team); break;
                case UnitIcon.Tank: Tank(m, o, team); break;
                case UnitIcon.SailShip: SailShip(m, o, team); break;
                case UnitIcon.Steamship: Steamship(m, o, team, carrier: false); break;
                case UnitIcon.Carrier: Steamship(m, o, team, carrier: true); break;
                case UnitIcon.Fighter: case UnitIcon.Jet: case UnitIcon.Bomber: Plane(m, o.Move(0, 0.3f, 0), team, icon); break;
                case UnitIcon.General: case UnitIcon.Scientist: case UnitIcon.Engineer:
                case UnitIcon.Merchant: case UnitIcon.Artist: case UnitIcon.Prophet:
                    Robed(m, o, team, icon);
                    break;
                case UnitIcon.Settler:
                    Figure(m, o.Move(-0.1f, 0, 0), team, icon);
                    Cart(m, o.Move(0.12f, 0, 0.02f));
                    break;
                case UnitIcon.Worker:
                case UnitIcon.Scout:
                    Figure(m, o.Move(0, 0, 0), team, icon);
                    break;
                default:
                    // A squad of three in a wedge.
                    Figure(m, o.Move(0, 0, 0.1f), team, icon);
                    Figure(m, o.Move(-0.12f, 0, -0.07f), team, icon);
                    Figure(m, o.Move(0.12f, 0, -0.07f), team, icon);
                    break;
            }
            if (air) m.Frustum(Frame.At(0, 0.035f, 0), 4, 0, 0.015f, 0.3f * ModelScale, 0.015f, Iron); // flight stand
            return m;
        }

        // ------------------------------------------------------------------ people

        static void Figure(MeshData m, Frame f, Rgb team, UnitIcon icon)
        {
            bool modern = icon == UnitIcon.Helmet;
            var legs = modern ? Olive * 0.8f : Cloth;
            var body = modern ? Rgb.Lerp(Olive, team, 0.35f) : team;
            m.Box(f.Move(0, 0.055f, 0), 0.032f, 0.055f, 0.022f, legs);
            m.Box(f.Move(0, 0.145f, 0), 0.042f, 0.042f, 0.028f, body, body * 1.1f);
            m.Gem(f.Move(0, 0.212f, 0), 0.026f, 0.03f, 0.026f, Skin);

            switch (icon)
            {
                case UnitIcon.Sword:
                    m.Box(f.Move(0, 0.232f, 0), 0.029f, 0.012f, 0.029f, Steel); // helm
                    m.Box(f.Move(-0.05f, 0.13f, 0.01f), 0.008f, 0.05f, 0.04f, team * 0.7f, team * 0.8f); // shield
                    m.Box(f.Move(0.055f, 0.19f, 0.02f).Rotate(0, -35), 0.006f, 0.07f, 0.004f, Steel);
                    break;
                case UnitIcon.Spear:
                    m.Box(f.Move(0, 0.232f, 0), 0.029f, 0.012f, 0.029f, Steel);
                    m.Box(f.Move(0.05f, 0.19f, 0.01f), 0.005f, 0.17f, 0.005f, Wood);
                    m.Frustum(f.Move(0.05f, 0.36f, 0.01f), 4, 0, 0.014f, 0.05f, 0, Steel);
                    m.Box(f.Move(-0.05f, 0.13f, 0.01f), 0.008f, 0.045f, 0.035f, team * 0.7f);
                    break;
                case UnitIcon.Bow:
                    for (int i = -1; i <= 1; i++)
                        m.Box(f.Move(0.052f, 0.17f + i * 0.05f, 0.03f - Math.Abs(i) * 0.02f).Rotate(0, i * 30), 0.005f, 0.028f, 0.005f, Wood);
                    m.Box(f.Move(-0.02f, 0.16f, -0.035f), 0.012f, 0.04f, 0.01f, WoodDark); // quiver
                    break;
                case UnitIcon.Crossbow:
                    m.Box(f.Move(0, 0.15f, 0.06f), 0.006f, 0.006f, 0.04f, Wood);
                    m.Box(f.Move(0, 0.15f, 0.095f), 0.04f, 0.005f, 0.005f, Wood);
                    m.Box(f.Move(0, 0.232f, 0), 0.029f, 0.012f, 0.029f, Steel);
                    break;
                case UnitIcon.Musket:
                case UnitIcon.Helmet:
                    m.Box(f.Move(0.045f, 0.17f, 0.03f).Rotate(0, -30), 0.005f, 0.09f, 0.005f, modern ? Iron : WoodDark);
                    if (modern) m.Frustum(f.Move(0, 0.225f, 0), 6, 0, 0.034f, 0.03f, 0.018f, Olive * 0.9f);
                    else m.Frustum(f.Move(0, 0.232f, 0), 3, 0, 0.034f, 0.035f, 0, Cloth); // tricorn
                    break;
                case UnitIcon.Worker:
                    m.Box(f.Move(0.05f, 0.14f, 0.03f).Rotate(0, -40), 0.005f, 0.08f, 0.005f, Wood);
                    m.Box(f.Move(0.05f, 0.14f, 0.03f).Rotate(0, -40).Move(0, 0.08f, 0), 0.035f, 0.007f, 0.007f, Iron);
                    m.Frustum(f.Move(0, 0.232f, 0), 8, 0, 0.045f, 0.012f, 0.02f, Rgb.Hex(0xD6B25E)); // straw hat
                    break;
                case UnitIcon.Scout:
                    m.Frustum(f.Move(0, 0.03f, -0.005f), 5, 0, 0.055f, 0.2f, 0.03f, Rgb.Hex(0x3E5A3A), Rgb.Hex(0x4F7048), caps: false); // cloak
                    m.Box(f.Move(0.045f, 0.12f, 0.02f), 0.004f, 0.12f, 0.004f, Wood); // staff
                    break;
                case UnitIcon.Settler:
                    m.Box(f.Move(0, 0.16f, -0.045f), 0.035f, 0.04f, 0.022f, Canvas * 0.85f); // pack
                    break;
            }
        }

        static void Robed(MeshData m, Frame f, Rgb team, UnitIcon icon)
        {
            var robe = icon == UnitIcon.General ? team : Rgb.Lerp(team, Canvas, 0.35f);
            m.Frustum(f, 6, 0, 0.065f, 0.2f, 0.03f, robe, robe * 1.1f);
            m.Frustum(f.Move(0, 0.02f, 0), 6, 0, 0.068f, 0.02f, 0.066f, Gold); // hem
            m.Gem(f.Move(0, 0.235f, 0), 0.03f, 0.034f, 0.03f, Skin);
            switch (icon)
            {
                case UnitIcon.General:
                    m.Box(f.Move(0.07f, 0.26f, 0), 0.004f, 0.12f, 0.004f, Wood);
                    m.Box(f.Move(0.1f, 0.34f, 0), 0.03f, 0.028f, 0.003f, Gold, Gold); // standard
                    m.Frustum(f.Move(0, 0.26f, 0), 5, 0, 0.034f, 0.03f, 0.036f, Gold); // crown
                    break;
                case UnitIcon.Scientist: m.Gem(f.Move(0.06f, 0.16f, 0.03f), 0.02f, 0.03f, 0.02f, Rgb.Hex(0x7FD8E0)); break;
                case UnitIcon.Engineer: m.Box(f.Move(0.06f, 0.14f, 0.03f), 0.03f, 0.004f, 0.03f, Canvas); break;
                case UnitIcon.Merchant: m.Box(f.Move(0.06f, 0.12f, 0.03f), 0.022f, 0.022f, 0.022f, Gold); break;
                case UnitIcon.Artist: m.Box(f.Move(0.07f, 0.15f, 0.03f).Rotate(0, -30), 0.004f, 0.05f, 0.004f, Wood); break;
                case UnitIcon.Prophet: m.Frustum(f.Move(0.06f, 0.16f, 0.03f), 5, 0, 0.018f, 0.05f, 0, Rgb.Hex(0xFFB347)); break;
            }
        }

        static void Rider(MeshData m, Frame f, Rgb team)
        {
            // Horse: body, four legs, neck and head, tail.
            m.Box(f.Move(0, 0.13f, 0), 0.04f, 0.035f, 0.1f, Horse, Horse * 1.1f);
            foreach (var (x, z) in new[] { (-0.025f, 0.07f), (0.025f, 0.07f), (-0.025f, -0.07f), (0.025f, -0.07f) })
                m.Box(f.Move(x, 0.05f, z), 0.01f, 0.05f, 0.01f, Horse * 0.8f);
            m.Box(f.Move(0, 0.19f, 0.11f).Rotate(0, -35), 0.022f, 0.05f, 0.02f, Horse);
            m.Box(f.Move(0, 0.225f, 0.15f), 0.02f, 0.018f, 0.04f, Horse);
            m.Box(f.Move(0, 0.15f, -0.105f).Rotate(0, 30), 0.008f, 0.035f, 0.008f, Cloth);
            // Rider in the owner's colour, lance forward.
            m.Box(f.Move(0, 0.21f, -0.01f), 0.036f, 0.04f, 0.025f, team, team * 1.1f);
            m.Gem(f.Move(0, 0.27f, -0.01f), 0.024f, 0.028f, 0.024f, Skin);
            m.Box(f.Move(0.045f, 0.24f, 0.08f).Rotate(0, 70), 0.004f, 0.13f, 0.004f, Wood);
        }

        // ------------------------------------------------------------------ machines

        static void Wheel(MeshData m, Frame f, float r, Rgb c) =>
            m.Frustum(f.Rotate(0, 0, 90), 8, -0.012f, r, 0.012f, r, c, c * 1.1f);

        static void Cart(MeshData m, Frame f)
        {
            m.Box(f.Move(0, 0.08f, 0), 0.05f, 0.025f, 0.08f, Wood);
            m.Frustum(f.Move(0, 0.1f, 0).Rotate(0, 90), 6, 0, 0.055f, 0.14f, 0.055f, Canvas, Canvas * 1.05f);
            Wheel(m, f.Move(-0.058f, 0.045f, 0), 0.045f, WoodDark);
            Wheel(m, f.Move(0.058f, 0.045f, 0), 0.045f, WoodDark);
        }

        static void Catapult(MeshData m, Frame f, Rgb team)
        {
            m.Box(f.Move(0, 0.06f, 0), 0.09f, 0.02f, 0.14f, Wood);
            m.Box(f.Move(-0.07f, 0.13f, -0.02f), 0.012f, 0.06f, 0.012f, WoodDark);
            m.Box(f.Move(0.07f, 0.13f, -0.02f), 0.012f, 0.06f, 0.012f, WoodDark);
            m.Box(f.Move(0, 0.18f, -0.02f), 0.08f, 0.01f, 0.01f, WoodDark);
            m.Box(f.Move(0, 0.15f, 0.04f).Rotate(0, -55), 0.012f, 0.14f, 0.012f, Wood);
            m.Box(f.Move(0, 0.26f, -0.05f), 0.035f, 0.015f, 0.035f, WoodDark);
            m.Box(f.Move(0, 0.08f, 0.13f), 0.05f, 0.02f, 0.012f, team);
            foreach (var z in new[] { -0.1f, 0.1f })
            {
                Wheel(m, f.Move(-0.1f, 0.05f, z), 0.05f, WoodDark);
                Wheel(m, f.Move(0.1f, 0.05f, z), 0.05f, WoodDark);
            }
            Figure(m, f.Move(-0.16f, 0, -0.14f), team, UnitIcon.Worker);
        }

        static void Cannon(MeshData m, Frame f, Rgb team)
        {
            m.Box(f.Move(0, 0.07f, -0.06f).Rotate(0, 15), 0.03f, 0.02f, 0.1f, Wood);
            m.Frustum(f.Move(0, 0.1f, -0.02f).Rotate(0, 78), 8, 0, 0.04f, 0.2f, 0.028f, Iron, Iron * 1.2f);
            Wheel(m, f.Move(-0.055f, 0.075f, 0), 0.075f, WoodDark);
            Wheel(m, f.Move(0.055f, 0.075f, 0), 0.075f, WoodDark);
            m.Box(f.Move(0, 0.03f, -0.16f), 0.03f, 0.02f, 0.02f, team);
            Figure(m, f.Move(-0.16f, 0, -0.1f), team, UnitIcon.Musket);
            Figure(m, f.Move(0.16f, 0, -0.1f), team, UnitIcon.Musket);
        }

        static void RocketTruck(MeshData m, Frame f, Rgb team)
        {
            var body = Rgb.Lerp(Olive, team, 0.3f);
            m.Box(f.Move(0, 0.07f, 0), 0.07f, 0.03f, 0.16f, body);
            m.Box(f.Move(0, 0.12f, 0.1f), 0.065f, 0.035f, 0.05f, body, body * 1.1f);
            m.Box(f.Move(0, 0.16f, -0.05f).Rotate(0, -25), 0.07f, 0.035f, 0.1f, Iron, Iron * 1.3f);
            foreach (var z in new[] { -0.1f, 0f, 0.11f })
            {
                Wheel(m, f.Move(-0.075f, 0.04f, z), 0.04f, Plinth);
                Wheel(m, f.Move(0.075f, 0.04f, z), 0.04f, Plinth);
            }
        }

        static void Ram(MeshData m, Frame f, Rgb team)
        {
            m.Box(f.Move(0, 0.06f, 0), 0.07f, 0.015f, 0.14f, Wood);
            // A-frame roof over the log.
            var hide = Rgb.Lerp(Rgb.Hex(0x8A6A4A), team, 0.35f); // hide-covered roof
            m.Quad(f.P(-0.08f, 0.07f, -0.14f), f.P(-0.08f, 0.07f, 0.14f), f.P(0, 0.18f, 0.14f), f.P(0, 0.18f, -0.14f), hide);
            m.Quad(f.P(0.08f, 0.07f, 0.14f), f.P(0.08f, 0.07f, -0.14f), f.P(0, 0.18f, -0.14f), f.P(0, 0.18f, 0.14f), hide * 0.85f);
            m.Frustum(f.Move(0, 0.11f, -0.2f).Rotate(0, 90), 6, 0, 0.025f, 0.38f, 0.025f, WoodDark);
            m.Gem(f.Move(0, 0.11f, 0.19f), 0.035f, 0.035f, 0.035f, Iron);
            foreach (var z in new[] { -0.1f, 0.1f })
            {
                Wheel(m, f.Move(-0.08f, 0.04f, z), 0.04f, WoodDark);
                Wheel(m, f.Move(0.08f, 0.04f, z), 0.04f, WoodDark);
            }
        }

        static void SiegeTower(MeshData m, Frame f, Rgb team)
        {
            m.Frustum(f.Move(0, 0.04f, 0), 4, 0, 0.1f, 0.34f, 0.07f, Wood, WoodDark, phase: (float)Math.PI / 4);
            m.Box(f.Move(0, 0.3f, 0.055f), 0.045f, 0.04f, 0.004f, team); // banner on the drawbridge
            foreach (var z in new[] { -0.08f, 0.08f })
            {
                Wheel(m, f.Move(-0.085f, 0.04f, z), 0.04f, WoodDark);
                Wheel(m, f.Move(0.085f, 0.04f, z), 0.04f, WoodDark);
            }
        }

        static void Tank(MeshData m, Frame f, Rgb team)
        {
            var hull = Rgb.Lerp(Olive, team, 0.35f);
            m.Box(f.Move(-0.085f, 0.045f, 0), 0.03f, 0.035f, 0.16f, Plinth); // tracks
            m.Box(f.Move(0.085f, 0.045f, 0), 0.03f, 0.035f, 0.16f, Plinth);
            m.Box(f.Move(0, 0.08f, 0), 0.08f, 0.03f, 0.15f, hull, hull * 1.1f);
            m.Frustum(f.Move(0, 0.11f, -0.02f), 6, 0, 0.07f, 0.05f, 0.055f, hull, hull * 1.15f);
            m.Frustum(f.Move(0, 0.135f, 0.03f).Rotate(0, 90), 6, 0, 0.012f, 0.19f, 0.01f, Iron);
        }

        // ------------------------------------------------------------------ ships and planes

        static void Hull(MeshData m, Frame f, float len, float beam, float depth, Rgb side, Rgb deck)
        {
            // Pointed bow at +z, square stern at −z.
            var p = new[]
            {
                f.P(-beam, depth, -len), f.P(beam, depth, -len), f.P(beam, depth, len * 0.55f), f.P(0, depth, len), f.P(-beam, depth, len * 0.55f),
            };
            var k = new[]
            {
                f.P(-beam * 0.6f, 0, -len * 0.9f), f.P(beam * 0.6f, 0, -len * 0.9f), f.P(beam * 0.6f, 0, len * 0.45f), f.P(0, 0, len * 0.85f), f.P(-beam * 0.6f, 0, len * 0.45f),
            };
            m.Tri(p[0], p[4], p[1], deck); m.Tri(p[1], p[4], p[2], deck); m.Tri(p[2], p[4], p[3], deck);
            for (int i = 0; i < 5; i++)
            {
                int j = (i + 1) % 5;
                m.Quad(p[i], p[j], k[j], k[i], side, side, side * 0.7f, side * 0.7f);
            }
        }

        static void SailShip(MeshData m, Frame f, Rgb team)
        {
            Hull(m, f, 0.3f, 0.09f, 0.08f, Wood, Rgb.Hex(0xB08A5E));
            foreach (var z in new[] { -0.08f, 0.1f })
            {
                m.Box(f.Move(0, 0.26f, z), 0.006f, 0.19f, 0.006f, WoodDark);
                // Square sail with the owner's colour band.
                m.Box(f.Move(0, 0.27f, z + 0.012f), 0.1f, 0.09f, 0.004f, Canvas);
                m.Box(f.Move(0, 0.27f, z + 0.018f), 0.1f, 0.022f, 0.003f, team);
            }
            m.Box(f.Move(0, 0.47f, 0.1f), 0.03f, 0.015f, 0.002f, team); // pennant
        }

        static void Steamship(MeshData m, Frame f, Rgb team, bool carrier)
        {
            Hull(m, f, 0.34f, 0.1f, 0.07f, Navy, Rgb.Hex(0x8C949C));
            m.Box(f.Move(0, 0.072f, 0), 0.098f, 0.004f, 0.3f, team * 0.9f); // painted stripe on deck
            if (carrier)
            {
                m.Box(f.Move(0, 0.1f, 0), 0.13f, 0.012f, 0.34f, Rgb.Hex(0x5E6670));
                m.Box(f.Move(0.09f, 0.16f, 0.02f), 0.02f, 0.05f, 0.06f, Navy);
                return;
            }
            m.Box(f.Move(0, 0.11f, -0.04f), 0.06f, 0.04f, 0.1f, Navy * 1.1f);
            m.Box(f.Move(0, 0.17f, -0.02f), 0.04f, 0.025f, 0.05f, Navy * 1.2f);
            m.Frustum(f.Move(0, 0.15f, -0.1f), 6, 0, 0.022f, 0.12f, 0.02f, Plinth, team);
            m.Frustum(f.Move(0, 0.08f, 0.16f), 6, 0, 0.035f, 0.035f, 0.03f, Iron); // turret
            m.Frustum(f.Move(0, 0.1f, 0.18f).Rotate(0, 90), 5, 0, 0.008f, 0.1f, 0.007f, Iron);
        }

        static void Plane(MeshData m, Frame f, Rgb team, UnitIcon icon)
        {
            float span = icon == UnitIcon.Bomber ? 0.3f : 0.22f, len = icon == UnitIcon.Bomber ? 0.2f : 0.16f;
            var body = icon == UnitIcon.Jet ? Rgb.Hex(0x9AA4AE) : Rgb.Lerp(Olive, Navy, 0.5f);
            m.Frustum(f.Move(0, 0, -len).Rotate(0, 90), 6, 0, 0.03f, len * 2f, 0.018f, body, body * 1.1f);
            if (icon == UnitIcon.Jet)
            {
                m.Tri(f.P(0, 0, len * 0.6f), f.P(span, 0, -len * 0.6f), f.P(-span, 0, -len * 0.6f), body * 1.05f);
                m.Tri(f.P(0, 0, len * 0.6f), f.P(-span, 0, -len * 0.6f), f.P(span, 0, -len * 0.6f), body * 0.8f);
            }
            else
            {
                m.Box(f.Move(0, 0, 0.02f), span, 0.006f, 0.04f, body, body * 1.1f);
                m.Box(f.Move(0, 0.01f, -len * 0.85f), span * 0.4f, 0.005f, 0.025f, body);
            }
            m.Box(f.Move(0, 0.035f, -len * 0.85f), 0.004f, 0.03f, 0.025f, body);
            // Roundels in the owner's colour on both wings.
            m.Box(f.Move(span * 0.6f, 0.008f, 0.0f), 0.03f, 0.002f, 0.025f, team);
            m.Box(f.Move(-span * 0.6f, 0.008f, 0.0f), 0.03f, 0.002f, 0.025f, team);
            if (icon == UnitIcon.Bomber)
                foreach (var x in new[] { -0.2f, -0.1f, 0.1f, 0.2f })
                    m.Frustum(f.Move(x, -0.012f, 0.02f).Rotate(0, 90), 6, 0, 0.014f, 0.07f, 0.012f, Iron);
        }
    }
}
