namespace FLLiteStudio.Core;

public sealed record InstrumentPreset(
    string Name,
    string Category,
    string Description,
    string ColorHex,
    int[] PreviewNotes,
    Func<ChannelModel> CreateChannel);

public static class InstrumentLibrary
{
    public static readonly string[] Categories =
    {
        "Todos",
        "Batería y Percusión",
        "Bajos y 808",
        "Pianos y Teclados",
        "Guitarras y Cuerdas",
        "Vientos y Metales",
        "Orquesta, Coros y Pads",
        "Sintetizadores y Leads",
        "Marimbas y Percusión Melódica",
        "Audio Propio (WAV)"
    };

    private static readonly int[] NoteC4 = { 60 };
    private static readonly int[] NoteBass = { 36 };
    private static readonly int[] NoteMid = { 64 };
    private static readonly int[] ChordCm = { 60, 63, 67 };
    private static readonly int[] ChordGtr = { 52, 59, 64, 67 };
    private static readonly int[] NoteHigh = { 72 };

    public static readonly InstrumentPreset[] All =
    {
        // ====================================================================
        // 1. BATERÍA Y PERCUSIÓN (14)
        // ====================================================================
        new("Bombo Trap Fuerte", "Batería y Percusión", "Kick moderno con pegada seca y grave potente", "#E05A47", NoteC4, () => new ChannelModel
        {
            Name = "Trap Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick, ColorHex = "#E05A47", MixerTrack = 1, Volume = 0.96f, Decay = 0.28f, Punch = 0.94f, Tone = 0.58f, Drive = 0.24f
        }),
        new("Bombo 808 Profundo", "Batería y Percusión", "Kick clásico 808 con cuerpo largo y subgrave", "#D94A4A", NoteC4, () => new ChannelModel
        {
            Name = "808 Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Kick808, ColorHex = "#D94A4A", MixerTrack = 1, Volume = 0.95f, Decay = 0.52f, Punch = 0.84f, Tone = 0.42f, Drive = 0.28f
        }),
        new("Bombo Boom-Bap Vintage", "Batería y Percusión", "Kick cálido estilo Hip-Hop 90s y Lo-Fi", "#C85A32", NoteC4, () => new ChannelModel
        {
            Name = "BoomBap Kick", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.KickBoomBap, ColorHex = "#C85A32", MixerTrack = 1, Volume = 0.92f, Decay = 0.26f, Punch = 0.65f, Tone = 0.35f, Drive = 0.32f
        }),
        new("Caja Estudio (Snare)", "Batería y Percusión", "Snare versátil con cuerpo y bordonera nítida", "#E8832A", NoteC4, () => new ChannelModel
        {
            Name = "Studio Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Snare, ColorHex = "#E8832A", MixerTrack = 2, Volume = 0.85f, Decay = 0.22f, Tone = 0.65f
        }),
        new("Caja Trap Corta", "Batería y Percusión", "Snare aguda y rápida ideal para Trap y Drill", "#F09038", NoteC4, () => new ChannelModel
        {
            Name = "Trap Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.SnareTrap, ColorHex = "#F09038", MixerTrack = 2, Volume = 0.86f, Decay = 0.14f, Tone = 0.82f, PitchSemitones = 3
        }),
        new("Caja Reggaetón (Dembow)", "Batería y Percusión", "Snare seca con golpe clásico de Reggaetón", "#E8702A", NoteC4, () => new ChannelModel
        {
            Name = "Dembow Snare", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.SnareReggaeton, ColorHex = "#E8702A", MixerTrack = 2, Volume = 0.88f, Decay = 0.16f, Tone = 0.72f, Punch = 0.88f
        }),
        new("Palma 808 (Clap)", "Batería y Percusión", "Clap con triple golpe clásico de estudio", "#E59838", NoteC4, () => new ChannelModel
        {
            Name = "808 Clap", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Clap, ColorHex = "#E59838", MixerTrack = 2, Volume = 0.86f, Decay = 0.24f, Tone = 0.74f
        }),
        new("Hi-Hat Cerrado (Tight)", "Batería y Percusión", "Platillo cerrado corto ideal para rolls", "#D9A826", NoteC4, () => new ChannelModel
        {
            Name = "Closed Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.ClosedHat, ColorHex = "#D9A826", MixerTrack = 3, Volume = 0.68f, Decay = 0.08f, Tone = 0.80f
        }),
        new("Hi-Hat Abierto (Open)", "Batería y Percusión", "Platillo abierto brillante para contratiempos", "#C4B428", NoteC4, () => new ChannelModel
        {
            Name = "Open Hat", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.OpenHat, ColorHex = "#C4B428", MixerTrack = 3, Volume = 0.60f, Decay = 0.30f, Tone = 0.76f
        }),
        new("Aro de Caja (Rimshot)", "Batería y Percusión", "Golpe seco de madera para Afrobeat y Lo-Fi", "#46C273", NoteC4, () => new ChannelModel
        {
            Name = "Rimshot", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Rimshot, ColorHex = "#46C273", MixerTrack = 8, Volume = 0.82f, Decay = 0.07f, Tone = 0.70f
        }),
        new("Cencerro Phonk (808 Cowbell)", "Batería y Percusión", "Cencerro metálico afinable clásico de Drift Phonk", "#E05297", NoteC4, () => new ChannelModel
        {
            Name = "Phonk Cowbell", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Cowbell, ColorHex = "#E05297", MixerTrack = 8, Volume = 0.84f, Decay = 0.26f, Tone = 0.78f, Drive = 0.35f
        }),
        new("Bongo / Percusión Latina", "Batería y Percusión", "Percusión resonante para Reggaetón y House", "#29B6A8", NoteC4, () => new ChannelModel
        {
            Name = "Latin Perc", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Perc, ColorHex = "#29B6A8", MixerTrack = 8, Volume = 0.82f, Decay = 0.18f, Tone = 0.62f
        }),
        new("Tom de Batería", "Batería y Percusión", "Tom grave afinable para redobles y fills", "#3B9EFF", NoteC4, () => new ChannelModel
        {
            Name = "Floor Tom", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Tom, ColorHex = "#3B9EFF", MixerTrack = 8, Volume = 0.86f, Decay = 0.34f, Tone = 0.55f
        }),
        new("Shaker / Maraca", "Batería y Percusión", "Textura rítmica suave para House y Pop", "#86C263", NoteC4, () => new ChannelModel
        {
            Name = "Shaker", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Shaker, ColorHex = "#86C263", MixerTrack = 3, Volume = 0.58f, Decay = 0.11f, Tone = 0.75f
        }),
        new("Platillo Crash", "Batería y Percusión", "Crash brillante para entradas de estribillo", "#5BB4F5", NoteC4, () => new ChannelModel
        {
            Name = "Crash Cymbal", Type = InstrumentType.DrumSynth, DrumKind = DrumKind.Crash, ColorHex = "#5BB4F5", MixerTrack = 3, Volume = 0.62f, Decay = 1.10f, Tone = 0.75f
        }),

        // ====================================================================
        // 2. BAJOS Y 808 (8)
        // ====================================================================
        new("808 Spinz (Trap Clásico)", "Bajos y 808", "El 808 más usado en Trap: pegada y cola limpia", "#E05297", NoteBass, () => new ChannelModel
        {
            Name = "808 Spinz", Type = InstrumentType.Bass808, ColorHex = "#E05297", MixerTrack = 4, Volume = 0.94f, Drive = 0.42f, Punch = 0.82f, CutoffHz = 900f, Decay = 1.15f, Release = 0.18f
        }),
        new("808 Phonk Distorsionado", "Bajos y 808", "808 saturado y agresivo que rompe en parlantes", "#D93860", NoteBass, () => new ChannelModel
        {
            Name = "808 Phonk", Type = InstrumentType.Bass808, ColorHex = "#D93860", MixerTrack = 4, Volume = 0.92f, Drive = 0.82f, Punch = 0.92f, CutoffHz = 2200f, Decay = 0.95f, Release = 0.14f
        }),
        new("Sub Bass Puro (Reggaetón / Lo-Fi)", "Bajos y 808", "Subgrave limpio y redondo sin distorsión", "#29B6A8", NoteBass, () => new ChannelModel
        {
            Name = "Pure Sub Bass", Type = InstrumentType.Bass808, ColorHex = "#29B6A8", MixerTrack = 4, Volume = 0.88f, Drive = 0.08f, Punch = 0.18f, CutoffHz = 360f, Decay = 1.65f, Release = 0.22f
        }),
        new("808 Zaytoven (Suave y Profundo)", "Bajos y 808", "808 melódico con armónicos cálidos", "#C44FB8", NoteBass, () => new ChannelModel
        {
            Name = "808 Zaytoven", Type = InstrumentType.Bass808, ColorHex = "#C44FB8", MixerTrack = 4, Volume = 0.92f, Drive = 0.28f, Punch = 0.65f, CutoffHz = 680f, Decay = 1.40f, Release = 0.24f
        }),
        new("Reese Bass (Oscuro / Drill / DnB)", "Bajos y 808", "Bajo ancho desafinado para intros oscuras", "#7B6BF2", NoteBass, () => new ChannelModel
        {
            Name = "Reese Bass", Type = InstrumentType.SuperSaw, ColorHex = "#7B6BF2", MixerTrack = 4, Volume = 0.80f, Detune = 36f, CutoffHz = 540f, Resonance = 2.0f, FilterEnv = 450f, Attack = 0.04f, Decay = 0.6f, Sustain = 0.85f, Release = 0.28f
        }),
        new("Bajo House FM (Donk / Pluck)", "Bajos y 808", "Bajo percusivo clásico de House y Deep House", "#3B9EFF", NoteBass, () => new ChannelModel
        {
            Name = "House FM Bass", Type = InstrumentType.SytrusFM, ColorHex = "#3B9EFF", MixerTrack = 4, Volume = 0.84f, FmRatio = 1.0f, FmIndex = 3.8f, FmDecay = 0.14f, CutoffHz = 1800f, Attack = 0.004f, Decay = 0.22f, Sustain = 0.12f, Release = 0.14f
        }),
        new("Bajo Ácido 303 (Techno)", "Bajos y 808", "Bajo resonante estilo Roland TB-303", "#46C273", NoteBass, () => new ChannelModel
        {
            Name = "Acid 303 Bass", Type = InstrumentType.Osc3x, ColorHex = "#46C273", MixerTrack = 4, Volume = 0.82f, Wave1 = WaveKind.Saw, Wave2 = WaveKind.Square, Coarse2 = 0, Osc2Mix = 0.25f, CutoffHz = 650f, Resonance = 6.8f, FilterEnv = 3600f, Attack = 0.004f, Decay = 0.18f, Sustain = 0.18f, Release = 0.10f
        }),
        new("Bajo Eléctrico (Finger Bass)", "Bajos y 808", "Bajo de cuerdas reales para Funk, Pop y Rock", "#C87D2F", NoteBass, () => new ChannelModel
        {
            Name = "Electric Bass", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.ElectricBass, ColorHex = "#C87D2F", MixerTrack = 4, Volume = 0.90f, Tone = 0.42f, Decay = 1.25f, CutoffHz = 1600f
        }),

        // ====================================================================
        // 3. PIANOS Y TECLADOS (8)
        // ====================================================================
        new("Piano de Cola (Grand Piano)", "Pianos y Teclados", "Piano acústico brillante para acordes y baladas", "#B564E8", ChordCm, () => new ChannelModel
        {
            Name = "Grand Piano", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.GrandPiano, ColorHex = "#B564E8", MixerTrack = 6, Volume = 0.82f, Tone = 0.72f, Decay = 1.65f, Release = 0.45f
        }),
        new("Piano Lo-Fi Íntimo", "Pianos y Teclados", "Piano suave y opaco ideal para Chill y Trap triste", "#9B6CE0", ChordCm, () => new ChannelModel
        {
            Name = "Lo-Fi Piano", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.GrandPiano, ColorHex = "#9B6CE0", MixerTrack = 6, Volume = 0.80f, Tone = 0.34f, Decay = 1.90f, Release = 0.55f
        }),
        new("Piano Eléctrico Rhodes", "Pianos y Teclados", "Clásico Rhodes cálido para R&B, Lo-Fi y Neo-Soul", "#A855F7", ChordCm, () => new ChannelModel
        {
            Name = "Rhodes EP", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Rhodes, ColorHex = "#A855F7", MixerTrack = 6, Volume = 0.82f, Tone = 0.56f, Decay = 1.85f, Release = 0.50f
        }),
        new("Wurlitzer Vintage", "Pianos y Teclados", "Teclado eléctrico con mordida cálida estilo 70s", "#C064D9", ChordCm, () => new ChannelModel
        {
            Name = "Wurlitzer EP", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Wurlitzer, ColorHex = "#C064D9", MixerTrack = 6, Volume = 0.82f, Tone = 0.66f, Drive = 0.25f, Decay = 1.40f, Release = 0.35f
        }),
        new("Órgano Club / Hammond", "Pianos y Teclados", "Órgano con registros armónicos para House y Gospel", "#D98A36", ChordCm, () => new ChannelModel
        {
            Name = "Hammond Organ", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Organ, ColorHex = "#D98A36", MixerTrack = 6, Volume = 0.76f, Tone = 0.68f, Decay = 2.0f, Sustain = 0.90f, Release = 0.22f
        }),
        new("Clavicordio (Harpsichord)", "Pianos y Teclados", "Teclado barroco metálico muy usado en Trap oscuro", "#E0A83B", ChordCm, () => new ChannelModel
        {
            Name = "Harpsichord", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Harpsichord, ColorHex = "#E0A83B", MixerTrack = 6, Volume = 0.78f, Tone = 0.85f, Decay = 0.95f, Release = 0.25f
        }),
        new("Acordeón / Bandoneón", "Pianos y Teclados", "Sonido cálido de lengüeta para Cumbia, Tango y Pop", "#3DA88A", ChordCm, () => new ChannelModel
        {
            Name = "Accordion", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.Accordion, ColorHex = "#3DA88A", MixerTrack = 6, Volume = 0.78f, Tone = 0.65f, Attack = 0.035f, Sustain = 0.88f, Release = 0.25f
        }),
        new("Teclado Campana (Celesta)", "Pianos y Teclados", "Teclado cristalino mágico para melodías suaves", "#56B9F2", ChordCm, () => new ChannelModel
        {
            Name = "Celesta Bell", Type = InstrumentType.KeysPiano, KeysMode = KeysMode.BellKeys, ColorHex = "#56B9F2", MixerTrack = 7, Volume = 0.78f, Tone = 0.82f, Decay = 1.60f, Release = 0.50f
        }),

        // ====================================================================
        // 4. GUITARRAS Y CUERDAS PULSADAS (7 — Karplus-Strong Real!)
        // ====================================================================
        new("Guitarra Acústica (Nylon)", "Guitarras y Cuerdas", "Guitarra española / latina cálida con cuerda real", "#D9822B", ChordGtr, () => new ChannelModel
        {
            Name = "Guitarra Nylon", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.AcousticNylon, ColorHex = "#D9822B", MixerTrack = 5, Volume = 0.86f, Tone = 0.52f, Decay = 1.45f, CutoffHz = 3600f
        }),
        new("Guitarra Acústica (Acero)", "Guitarras y Cuerdas", "Guitarra folk brillante ideal para arpegios y Pop", "#E89A3C", ChordGtr, () => new ChannelModel
        {
            Name = "Guitarra Acero", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.AcousticSteel, ColorHex = "#E89A3C", MixerTrack = 5, Volume = 0.84f, Tone = 0.78f, Decay = 1.60f, CutoffHz = 7200f
        }),
        new("Guitarra Eléctrica Limpia", "Guitarras y Cuerdas", "Guitarra clean estilo Strat para R&B, Trap y Lo-Fi", "#3B9EFF", ChordGtr, () => new ChannelModel
        {
            Name = "Guitarra Clean", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.ElectricClean, ColorHex = "#3B9EFF", MixerTrack = 5, Volume = 0.84f, Tone = 0.68f, Decay = 1.75f, CutoffHz = 5400f
        }),
        new("Guitarra Eléctrica Rock", "Guitarras y Cuerdas", "Guitarra con overdrive para riffs y Power Chords", "#E05A47", ChordGtr, () => new ChannelModel
        {
            Name = "Guitarra Rock", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.ElectricRock, ColorHex = "#E05A47", MixerTrack = 5, Volume = 0.78f, Tone = 0.76f, Drive = 0.68f, Decay = 1.85f, CutoffHz = 4200f
        }),
        new("Arpa Clásica (Harp)", "Guitarras y Cuerdas", "Cuerdas dulces y cristalinas para arpegios épicos", "#73C6E8", ChordGtr, () => new ChannelModel
        {
            Name = "Arpa Clásica", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.Harp, ColorHex = "#73C6E8", MixerTrack = 7, Volume = 0.84f, Tone = 0.64f, Decay = 2.10f, CutoffHz = 6200f
        }),
        new("Koto / Cítara Oriental", "Guitarras y Cuerdas", "Cuerda oriental filosa muy usada en Trap y Drill", "#E05297", NoteHigh, () => new ChannelModel
        {
            Name = "Koto Oriental", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.Koto, ColorHex = "#E05297", MixerTrack = 7, Volume = 0.84f, Tone = 0.86f, Decay = 0.95f, CutoffHz = 8500f
        }),
        new("Pizzicato de Orquesta", "Guitarras y Cuerdas", "Violines pellizcados cortos para bases rítmicas", "#46C273", ChordCm, () => new ChannelModel
        {
            Name = "Pizzicato", Type = InstrumentType.PluckedString, PluckStyle = PluckStyle.Pizzicato, ColorHex = "#46C273", MixerTrack = 7, Volume = 0.86f, Tone = 0.58f, Decay = 0.45f, CutoffHz = 3800f
        }),

        // ====================================================================
        // 5. VIENTOS Y METALES (7)
        // ====================================================================
        new("Flauta Trap Oscura (Metro Flute)", "Vientos y Metales", "Flauta con aire y vibrato icónica del Trap moderno", "#29B6A8", NoteHigh, () => new ChannelModel
        {
            Name = "Trap Flute", Type = InstrumentType.BrassWind, WindStyle = WindStyle.DarkFlute, ColorHex = "#29B6A8", MixerTrack = 5, Volume = 0.82f, Tone = 0.55f, VibratoDepth = 0.22f, VibratoRateHz = 5.4f, Attack = 0.045f, Release = 0.30f
        }),
        new("Flauta Traversa Clásica", "Vientos y Metales", "Flauta dulce y aireada para melodías expresivas", "#58C9B9", NoteHigh, () => new ChannelModel
        {
            Name = "Flauta Traversa", Type = InstrumentType.BrassWind, WindStyle = WindStyle.Flute, ColorHex = "#58C9B9", MixerTrack = 5, Volume = 0.80f, Tone = 0.68f, VibratoDepth = 0.18f, VibratoRateHz = 5.0f, Attack = 0.04f, Release = 0.25f
        }),
        new("Trompeta Solista", "Vientos y Metales", "Metal brillante con ataque expresivo", "#E8B02A", NoteMid, () => new ChannelModel
        {
            Name = "Trompeta", Type = InstrumentType.BrassWind, WindStyle = WindStyle.Trumpet, ColorHex = "#E8B02A", MixerTrack = 5, Volume = 0.78f, Tone = 0.76f, VibratoDepth = 0.14f, Attack = 0.035f, Release = 0.20f
        }),
        new("Brass Hit Épico (Trap / Cine)", "Vientos y Metales", "Golpe de metales potentes para drops y coros", "#E8832A", ChordCm, () => new ChannelModel
        {
            Name = "Trap Brass Hit", Type = InstrumentType.BrassWind, WindStyle = WindStyle.TrapBrass, ColorHex = "#E8832A", MixerTrack = 5, Volume = 0.84f, Tone = 0.85f, Drive = 0.30f, Attack = 0.02f, Decay = 0.45f, Release = 0.32f
        }),
        new("Saxofón Suave (Jazz / Lo-Fi)", "Vientos y Metales", "Saxo cálido con vibrato expresivo", "#D9923B", NoteMid, () => new ChannelModel
        {
            Name = "Saxofón", Type = InstrumentType.BrassWind, WindStyle = WindStyle.Saxophone, ColorHex = "#D9923B", MixerTrack = 5, Volume = 0.80f, Tone = 0.60f, VibratoDepth = 0.25f, VibratoRateHz = 5.6f, Attack = 0.05f, Release = 0.25f
        }),
        new("Clarinete", "Vientos y Metales", "Viento de madera suave y hueco", "#7B8CE0", NoteMid, () => new ChannelModel
        {
            Name = "Clarinete", Type = InstrumentType.BrassWind, WindStyle = WindStyle.Clarinet, ColorHex = "#7B8CE0", MixerTrack = 5, Volume = 0.78f, Tone = 0.50f, VibratoDepth = 0.10f, Attack = 0.04f, Release = 0.22f
        }),
        new("Trombón Grave", "Vientos y Metales", "Metal profundo para bajos orquestales", "#C87528", NoteBass, () => new ChannelModel
        {
            Name = "Trombón", Type = InstrumentType.BrassWind, WindStyle = WindStyle.Trombone, ColorHex = "#C87528", MixerTrack = 5, Volume = 0.82f, Tone = 0.55f, Attack = 0.055f, Release = 0.26f
        }),

        // ====================================================================
        // 6. ORQUESTA, COROS Y PADS (7)
        // ====================================================================
        new("Cuerdas de Orquesta (Strings)", "Orquesta, Coros y Pads", "Sección de violines ancha para acordes cinematográficos", "#7B6BF2", ChordCm, () => new ChannelModel
        {
            Name = "Orquesta Strings", Type = InstrumentType.StringsPad, PadStyle = PadStyle.OrchestraStrings, ColorHex = "#7B6BF2", MixerTrack = 6, Volume = 0.76f, Detune = 22f, CutoffHz = 4400f, Attack = 0.24f, Release = 0.75f
        }),
        new("Violín Solista", "Orquesta, Coros y Pads", "Violín expresivo con vibrato natural", "#9A6BF2", NoteHigh, () => new ChannelModel
        {
            Name = "Violín Solo", Type = InstrumentType.StringsPad, PadStyle = PadStyle.ViolinSolo, ColorHex = "#9A6BF2", MixerTrack = 5, Volume = 0.78f, CutoffHz = 5600f, VibratoDepth = 0.26f, VibratoRateHz = 5.8f, Attack = 0.14f, Release = 0.42f
        }),
        new("Violonchelo (Cello)", "Orquesta, Coros y Pads", "Cuerda grave y profunda con mucho cuerpo", "#6B5CE0", NoteBass, () => new ChannelModel
        {
            Name = "Cello", Type = InstrumentType.StringsPad, PadStyle = PadStyle.Cello, ColorHex = "#6B5CE0", MixerTrack = 6, Volume = 0.82f, CutoffHz = 2400f, VibratoDepth = 0.18f, Attack = 0.16f, Release = 0.50f
        }),
        new("Coro Vocal (Choir Aah)", "Orquesta, Coros y Pads", "Coro etéreo con formantes vocales para Trap/Épico", "#E068A8", ChordCm, () => new ChannelModel
        {
            Name = "Coro Vocal", Type = InstrumentType.StringsPad, PadStyle = PadStyle.ChoirAah, ColorHex = "#E068A8", MixerTrack = 6, Volume = 0.80f, Detune = 18f, CutoffHz = 2800f, Attack = 0.30f, Release = 0.90f
        }),
        new("Pad Espacial / Ambient", "Orquesta, Coros y Pads", "Colchón atmosférico brillante para intros y breaks", "#3B9EFF", ChordCm, () => new ChannelModel
        {
            Name = "Space Pad", Type = InstrumentType.StringsPad, PadStyle = PadStyle.SpacePad, ColorHex = "#3B9EFF", MixerTrack = 6, Volume = 0.74f, Detune = 30f, CutoffHz = 3400f, Attack = 0.42f, Release = 1.20f
        }),
        new("Pad Cinta Lo-Fi (Warm Tape)", "Orquesta, Coros y Pads", "Pad cálido con leve desafinación analógica", "#B564E8", ChordCm, () => new ChannelModel
        {
            Name = "Vintage Pad", Type = InstrumentType.StringsPad, PadStyle = PadStyle.VintageTapePad, ColorHex = "#B564E8", MixerTrack = 6, Volume = 0.76f, Detune = 24f, CutoffHz = 1900f, VibratoDepth = 0.22f, VibratoRateHz = 2.2f, Attack = 0.25f, Release = 0.95f
        }),
        new("Pad Oscuro Trap", "Orquesta, Coros y Pads", "Ambiente tenso y filtrado para beats oscuros", "#5B5EA6", ChordCm, () => new ChannelModel
        {
            Name = "Dark Trap Pad", Type = InstrumentType.StringsPad, PadStyle = PadStyle.DarkTrapPad, ColorHex = "#5B5EA6", MixerTrack = 6, Volume = 0.76f, Detune = 28f, CutoffHz = 1350f, Attack = 0.32f, Release = 1.10f
        }),

        // ====================================================================
        // 7. SINTETIZADORES Y LEADS (9)
        // ====================================================================
        new("FLEX EDM SuperSaw", "Sintetizadores y Leads", "Acordes gigantes estilo EDM, Progressive y Future Bass", "#3B9EFF", ChordCm, () => new ChannelModel
        {
            Name = "EDM SuperSaw", Type = InstrumentType.SuperSaw, ColorHex = "#3B9EFF", MixerTrack = 5, Volume = 0.74f, Detune = 28f, CutoffHz = 5600f, Resonance = 1.5f, FilterEnv = 2200f, Attack = 0.01f, Decay = 0.38f, Sustain = 0.75f, Release = 0.35f
        }),
        new("Sytrus Campana Oscura (Trap Bell)", "Sintetizadores y Leads", "Campana FM oscura clásica de Trap", "#47B5FF", NoteHigh, () => new ChannelModel
        {
            Name = "Sytrus Dark Bell", Type = InstrumentType.SytrusFM, ColorHex = "#47B5FF", MixerTrack = 5, Volume = 0.72f, FmRatio = 3.0f, FmIndex = 3.2f, FmDecay = 0.30f, CutoffHz = 5800f, Attack = 0.004f, Decay = 0.55f, Sustain = 0.14f, Release = 0.45f
        }),
        new("Sytrus Pluck Melódico", "Sintetizadores y Leads", "Pluck rápido para arpegios de EDM, Pop y Reggaetón", "#22C1D6", NoteHigh, () => new ChannelModel
        {
            Name = "Sytrus Pluck", Type = InstrumentType.SytrusFM, ColorHex = "#22C1D6", MixerTrack = 7, Volume = 0.74f, FmRatio = 2.0f, FmIndex = 2.4f, FmDecay = 0.15f, CutoffHz = 8200f, Attack = 0.003f, Decay = 0.24f, Sustain = 0.06f, Release = 0.22f
        }),
        new("Lead Synthwave 80s", "Sintetizadores y Leads", "Lead retro analógico estilo años 80", "#E05297", NoteMid, () => new ChannelModel
        {
            Name = "Synthwave Lead", Type = InstrumentType.Osc3x, ColorHex = "#E05297", MixerTrack = 5, Volume = 0.76f, Wave1 = WaveKind.Saw, Wave2 = WaveKind.Saw, Coarse2 = 0, Detune = 18f, Osc2Mix = 0.65f, CutoffHz = 3600f, Resonance = 2.2f, FilterEnv = 2000f, Attack = 0.01f, Decay = 0.3f, Sustain = 0.72f, Release = 0.30f
        }),
        new("Silbido G-Funk / West Coast", "Sintetizadores y Leads", "Lead agudo suave estilo Dr. Dre / Hip-Hop clásico", "#46C273", NoteHigh, () => new ChannelModel
        {
            Name = "G-Funk Lead", Type = InstrumentType.Osc3x, ColorHex = "#46C273", MixerTrack = 5, Volume = 0.74f, Wave1 = WaveKind.Sine, Wave2 = WaveKind.Triangle, Coarse2 = 12, Osc2Mix = 0.25f, CutoffHz = 6000f, Resonance = 1.0f, Attack = 0.015f, Sustain = 0.85f, Release = 0.20f
        }),
        new("Chiptune 8-Bit (NES Retro)", "Sintetizadores y Leads", "Onda cuadrada pura estilo videojuegos retro", "#E8B02A", NoteHigh, () => new ChannelModel
        {
            Name = "8-Bit Retro", Type = InstrumentType.Osc3x, ColorHex = "#E8B02A", MixerTrack = 7, Volume = 0.70f, Wave1 = WaveKind.Square, Wave2 = WaveKind.Square, Coarse2 = 12, Detune = 0f, Osc2Mix = 0.25f, CutoffHz = 14000f, Resonance = 0.7f, FilterEnv = 0f, Attack = 0.002f, Decay = 0.15f, Sustain = 0.70f, Release = 0.05f
        }),
        new("Chiptune Gameboy Arp", "Sintetizadores y Leads", "Sonido retro cortito ideal para melodías rápidas", "#9ACD32", NoteHigh, () => new ChannelModel
        {
            Name = "Gameboy Pluck", Type = InstrumentType.Osc3x, ColorHex = "#9ACD32", MixerTrack = 7, Volume = 0.72f, Wave1 = WaveKind.Square, Wave2 = WaveKind.Triangle, Coarse2 = 0, Osc2Mix = 0.35f, CutoffHz = 12000f, Attack = 0.002f, Decay = 0.16f, Sustain = 0.08f, Release = 0.06f
        }),
        new("Lead Hyperpop / Rage", "Sintetizadores y Leads", "Sintetizador agresivo y abierto estilo Playboi Carti / Rage", "#FF5C8A", ChordCm, () => new ChannelModel
        {
            Name = "Rage Lead", Type = InstrumentType.SuperSaw, ColorHex = "#FF5C8A", MixerTrack = 5, Volume = 0.76f, Detune = 42f, CutoffHz = 9500f, Resonance = 3.0f, FilterEnv = 3500f, Attack = 0.005f, Decay = 0.28f, Sustain = 0.80f, Release = 0.22f
        }),
        new("Pluck Reggaetón Moderno", "Sintetizadores y Leads", "Pluck cálido y redondo perfecto para acordes de Reggaetón", "#F09038", ChordCm, () => new ChannelModel
        {
            Name = "Reggaetón Pluck", Type = InstrumentType.Osc3x, ColorHex = "#F09038", MixerTrack = 7, Volume = 0.80f, Wave1 = WaveKind.Triangle, Wave2 = WaveKind.Saw, Coarse2 = 0, Osc2Mix = 0.45f, CutoffHz = 950f, Resonance = 1.4f, FilterEnv = 3200f, Attack = 0.004f, Decay = 0.22f, Sustain = 0.05f, Release = 0.18f
        }),

        // ====================================================================
        // 8. MARIMBAS, KALIMBA Y PERCUSIÓN MELÓDICA (7)
        // ====================================================================
        new("Marimba de Madera", "Marimbas y Percusión Melódica", "Láminas de madera cálidas para Pop, Reggaetón y Afrobeat", "#D9822B", ChordCm, () => new ChannelModel
        {
            Name = "Marimba", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.Marimba, ColorHex = "#D9822B", MixerTrack = 7, Volume = 0.86f, Tone = 0.55f, Decay = 0.55f, Release = 0.25f
        }),
        new("Kalimba (Afrobeat / Lo-Fi)", "Marimbas y Percusión Melódica", "Piano de pulgar africano dulce e hipnótico", "#29B6A8", NoteHigh, () => new ChannelModel
        {
            Name = "Kalimba", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.Kalimba, ColorHex = "#29B6A8", MixerTrack = 7, Volume = 0.84f, Tone = 0.65f, Decay = 0.85f, Release = 0.35f
        }),
        new("Vibráfono Jazz", "Marimbas y Percusión Melódica", "Láminas metálicas suaves con leve trémolo", "#5BB4F5", ChordCm, () => new ChannelModel
        {
            Name = "Vibráfono", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.Vibraphone, ColorHex = "#5BB4F5", MixerTrack = 7, Volume = 0.80f, Tone = 0.60f, Decay = 1.75f, VibratoDepth = 0.30f, VibratoRateHz = 4.8f, Release = 0.60f
        }),
        new("Steel Drum (Tropical / Dancehall)", "Marimbas y Percusión Melódica", "Tambor metálico caribeño para Reggaetón y Tropical House", "#46C273", ChordCm, () => new ChannelModel
        {
            Name = "Steel Drum", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.SteelDrum, ColorHex = "#46C273", MixerTrack = 7, Volume = 0.84f, Tone = 0.78f, Decay = 0.75f, Release = 0.30f
        }),
        new("Xilófono Brillante", "Marimbas y Percusión Melódica", "Golpe agudo y definido para melodías rápidas", "#E8B02A", NoteHigh, () => new ChannelModel
        {
            Name = "Xilófono", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.Xylophone, ColorHex = "#E8B02A", MixerTrack = 7, Volume = 0.82f, Tone = 0.85f, Decay = 0.32f, Release = 0.15f
        }),
        new("Campanas Tubulares (Iglesia)", "Marimbas y Percusión Melódica", "Campanas profundas y solemnes para intros épicas", "#B564E8", NoteMid, () => new ChannelModel
        {
            Name = "Campanas Iglesia", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.TubularBell, ColorHex = "#B564E8", MixerTrack = 7, Volume = 0.82f, Tone = 0.72f, Decay = 2.40f, Release = 0.90f
        }),
        new("Caja Musical (Music Box)", "Marimbas y Percusión Melódica", "Sonido agudo nostálgico ideal para melodías de terror/Trap", "#85C7F2", NoteHigh, () => new ChannelModel
        {
            Name = "Caja Musical", Type = InstrumentType.WorldMallet, MalletStyle = MalletStyle.MusicBox, ColorHex = "#85C7F2", MixerTrack = 7, Volume = 0.78f, Tone = 0.88f, Decay = 1.35f, Release = 0.50f
        }),

        // ====================================================================
        // 9. AUDIO PROPIO (WAV SAMPLER)
        // ====================================================================
        new("Sampler WAV Personalizado", "Audio Propio (WAV)", "Carga cualquier archivo .wav desde tu PC y tócalo en el Piano Roll", "#38BDF8", NoteC4, () => new ChannelModel
        {
            Name = "Sampler WAV", Type = InstrumentType.WavSampler, ColorHex = "#38BDF8", MixerTrack = 8, Volume = 0.90f, CutoffHz = 18000f
        })
    };
}
