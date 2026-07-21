namespace RiffRoster;

public static class SeedData
{
    private const string EventId = "brutal-assault-2026";
    private static readonly TimeSpan CzechOffset = TimeSpan.FromHours(2);

    public static AppState Create()
    {
        var days = new List<FestivalDay>
        {
            CreateTuesday(),
            Day(new DateOnly(2026, 8, 5), "Wednesday", Wednesday),
            Day(new DateOnly(2026, 8, 6), "Thursday", Thursday),
            Day(new DateOnly(2026, 8, 7), "Friday", Friday),
            Day(new DateOnly(2026, 8, 8), "Saturday", Saturday)
        };

        return new AppState
        {
            DataVersion = 6,
            Events = [new FestivalEvent(EventId, "Brutal Assault 2026", "Josefov Fortress, Czechia", new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 8), days)],
            Users = [],
            Selections = []
        };
    }

    public static FestivalDay CreateTuesday() => Day(new DateOnly(2026, 8, 4), "Tuesday · Warm-up", Tuesday);

    private static FestivalDay Day(DateOnly date, string label, IReadOnlyList<Slot> slots)
    {
        var performances = slots.Select((slot, index) =>
        {
            var startDate = TimeOnly.Parse(slot.Start) < new TimeOnly(6, 0) ? date.AddDays(1) : date;
            var endDate = TimeOnly.Parse(slot.End) < new TimeOnly(6, 0) ? date.AddDays(1) : date;
            if (endDate < startDate) endDate = endDate.AddDays(1);
            var startsAt = new DateTimeOffset(startDate.ToDateTime(TimeOnly.Parse(slot.Start)), CzechOffset);
            var endsAt = new DateTimeOffset(endDate.ToDateTime(TimeOnly.Parse(slot.End)), CzechOffset);
            return new Performance($"ba26-{date:MMdd}-{index:000}", slot.Artist, slot.Stage, startsAt, endsAt);
        }).OrderBy(item => item.StartsAt).ToList();

        return new FestivalDay($"ba26-{date:MMdd}", date, label, performances);
    }

    private sealed record Slot(string Stage, string Start, string End, string Artist);

    private static Slot S(string stage, string start, string end, string artist) => new(stage, start, end, artist);

    private static readonly Slot[] Tuesday =
    [
        S("Obscure", "14:40", "15:10", "Mordloch"), S("Obscure", "15:30", "16:00", "Patriarcha"),
        S("Obscure", "16:15", "16:50", "Nahum"), S("Obscure", "17:10", "17:50", "První Hoře"),
        S("Obscure", "18:10", "18:50", "Filth"), S("Obscure", "19:10", "19:50", "Morax"),
        S("Obscure", "20:10", "20:50", "Ahab"), S("Obscure", "21:10", "21:55", "The Laws performing Sarcófago"),
        S("KAL", "22:00", "22:50", "Octopoulpe"), S("KAL", "23:30", "00:15", "Bolehlav")
    ];

    private static readonly Slot[] Wednesday =
    [
        S("Main A", "12:00", "12:30", "Guilt Trip"), S("Main B", "12:35", "13:15", "Onslaught"),
        S("Main A", "13:20", "14:00", "Unearth"), S("Obscure", "13:30", "14:05", "200 Stab Wounds"),
        S("Main B", "14:05", "14:45", "H2O"), S("Obscure", "14:30", "15:15", "Armored Saint"),
        S("Main A", "14:50", "15:35", "Venomed"), S("Obscure", "15:45", "16:25", "Novembers Doom"),
        S("Main B", "15:40", "16:20", "Metal Church"), S("Main A", "16:25", "17:10", "Crowbar"),
        S("Obscure", "16:55", "17:35", "Elder"), S("Main B", "17:15", "18:05", "Samael"),
        S("Octagon", "17:15", "18:00", "Dom Zly"), S("Obscure", "18:05", "18:50", "Coven"),
        S("Main A", "18:10", "19:05", "Nevermore"), S("Octagon", "18:30", "19:15", "Crippling Alcoholism"),
        S("Main B", "19:10", "20:00", "Fit for an Autopsy"), S("Obscure", "19:20", "20:05", "Kylesa"),
        S("Octagon", "19:45", "20:30", "Iotunn"), S("Main A", "20:05", "21:05", "Periphery"),
        S("Obscure", "20:40", "21:35", "Sólstafir"), S("Octagon", "21:00", "22:00", "Bölzer"),
        S("Main B", "21:10", "22:15", "Municipal Waste"), S("Obscure", "22:05", "22:55", "Kim Dracula"),
        S("Main A", "22:20", "23:25", "Triptykon"), S("Octagon", "22:30", "23:30", "Violent Magic Orchestra"),
        S("Obscure", "23:25", "00:15", "Protest the Hero"), S("Main B", "23:30", "00:30", "Amenra"),
        S("Main A", "00:35", "01:30", "Batushka"), S("Obscure", "00:45", "01:30", "Die Krupps"),
        S("Obscure", "02:00", "02:40", "Witch Club Satan"),
        S("KAL", "18:30", "19:20", "Hnilomorna"), S("KAL", "20:00", "20:50", "Kariti"),
        S("KAL", "21:30", "22:20", "Sand Nudes"), S("KAL", "23:00", "00:00", "The Mon"),
        S("KAL", "00:30", "03:30", "Batsave DJ's")
    ];

    private static readonly Slot[] Thursday =
    [
        S("Main B", "10:30", "11:00", "Cryptic Shift"), S("Main A", "11:05", "11:35", "Baest"),
        S("Main B", "11:40", "12:15", "The Casualties"), S("Main A", "12:20", "12:55", "Deceased"),
        S("Main B", "13:00", "13:35", "Sanguisugabogg"), S("Main A", "13:40", "14:15", "Illdisposed"),
        S("Obscure", "14:00", "14:35", "Castle Rat"), S("Main B", "14:20", "15:05", "Hocico"),
        S("Obscure", "15:05", "15:45", "Blackbraid"), S("Main A", "15:10", "15:55", "Kittie"),
        S("Main B", "16:00", "16:45", "Bleed From Within"), S("Obscure", "16:15", "16:55", "III Niño"),
        S("Main A", "16:50", "17:35", "Thy Art Is Murder"), S("Obscure", "17:25", "18:05", "Death Angel"),
        S("Octagon", "17:30", "18:00", "Slope"), S("Main B", "17:40", "18:45", "Waltari"),
        S("Octagon", "18:30", "19:15", "Psychonaut"), S("Obscure", "18:35", "19:20", "Terror"),
        S("Main A", "18:50", "19:50", "Septicflesh"), S("Octagon", "19:45", "20:30", "Wyatt E."),
        S("Main B", "19:55", "21:00", "Body Count feat. Ice-T"), S("Obscure", "19:50", "20:35", "Draconian"),
        S("Main A", "21:05", "22:10", "Amorphis"), S("Obscure", "21:05", "21:55", "Deicide"),
        S("Octagon", "21:00", "21:50", "Lvmen"), S("Main B", "22:15", "23:25", "Carpenter Brut"),
        S("Obscure", "22:25", "23:15", "Aura Noir"), S("Octagon", "22:30", "23:30", "Antimatter"),
        S("Main A", "23:30", "00:30", "Alcest"), S("Obscure", "23:45", "00:30", "Fu Manchu"),
        S("Main B", "00:35", "01:30", "Immolation"), S("Obscure", "01:00", "01:40", "Front Line Assembly"),
        S("Obscure", "02:10", "02:50", "Tormentor"),
        S("KAL", "18:30", "19:20", "Petrolio"), S("KAL", "20:00", "20:50", "Ovo"),
        S("KAL", "21:30", "22:20", "Naher Einer"), S("KAL", "22:50", "23:50", "Blush Response"),
        S("KAL", "00:00", "01:30", "Philipp Strobel"), S("KAL", "01:30", "03:30", "Whydie?")
    ];

    private static readonly Slot[] Friday =
    [
        S("Main A", "10:30", "11:00", "Altars Ablaze"), S("Main B", "11:05", "11:40", "Allt"),
        S("Main A", "11:45", "12:20", "Internal Bleeding"), S("Main B", "12:25", "13:00", "TBC"),
        S("Main A", "13:05", "13:40", "Angelus Apatrida"), S("Main B", "13:45", "14:25", "Ratos de Porão"),
        S("Obscure", "14:30", "15:10", "Saor"), S("Main A", "14:30", "15:10", "Signs of the Swarm"),
        S("Main B", "15:15", "15:55", "Snět"), S("Obscure", "15:35", "16:15", "Kraanium"),
        S("Main A", "16:00", "16:50", "Corrosion of Conformity"), S("Obscure", "16:45", "17:25", "Thy Catafalque"),
        S("Main B", "16:55", "17:45", "Animals as Leaders"), S("Octagon", "17:30", "18:00", "Chaotian"),
        S("Main A", "17:50", "18:45", "Coroner"), S("Obscure", "17:55", "18:40", "Arthur Brown"),
        S("Octagon", "18:30", "19:15", "Dusk"), S("Main B", "18:50", "19:40", "Paleface Swiss"),
        S("Obscure", "19:10", "20:00", "Master Boot Record"), S("Main A", "19:45", "20:50", "Clawfinger"),
        S("Octagon", "19:45", "20:30", "Sadistic Intent"), S("Obscure", "20:30", "21:25", "Grave"),
        S("Main B", "20:55", "22:10", "Primus"), S("Octagon", "21:00", "22:00", "A.A. Williams"),
        S("Main A", "22:15", "23:15", "Eivør"), S("Obscure", "22:00", "22:50", "Misþyrming"),
        S("Octagon", "22:30", "23:30", "Mortal Sin"), S("Main B", "23:20", "00:20", "Katatonia"),
        S("Obscure", "23:20", "00:10", "Marduk"), S("Main A", "00:25", "01:25", "Old Man's Child"),
        S("Obscure", "00:40", "01:25", "Vio-Lence"), S("Obscure", "01:55", "02:40", "Pig"),
        S("KAL", "18:30", "19:10", "Utarm"), S("KAL", "20:00", "20:50", "Draugveil"),
        S("KAL", "21:30", "22:30", "Nadja"), S("KAL", "22:50", "23:35", "Jadran"),
        S("KAL", "00:00", "00:40", "Arottenbit"), S("KAL", "01:00", "02:00", "The Goondock Saint"),
        S("KAL", "02:20", "03:30", "Celina")
    ];

    private static readonly Slot[] Saturday =
    [
        S("Main A", "10:30", "11:00", "Deserted Fear"), S("Main B", "11:05", "11:40", "Vulvodynia"),
        S("Main A", "11:45", "12:20", "Djerv"), S("Main B", "12:25", "13:00", "Heavy//Hitter"),
        S("Main A", "13:05", "13:45", "Revocation"), S("Main B", "13:50", "14:30", "Sacred Reich"),
        S("Obscure", "14:30", "15:10", "The 3rd and the Mortal"), S("Main A", "14:35", "15:15", "Monstrosity"),
        S("Main B", "15:20", "16:00", "Demolition Hammer"), S("Obscure", "15:40", "16:15", "Coffins"),
        S("Main A", "16:05", "16:50", "PeelingFlesh"), S("Octagon", "16:00", "16:30", "Brutal Kids"),
        S("Obscure", "16:45", "17:20", "Wormed"), S("Main B", "16:55", "17:45", "Imminence"),
        S("Octagon", "17:30", "18:00", "Conjurer"), S("Main A", "17:50", "18:45", "Insomnium"),
        S("Obscure", "17:50", "18:40", "Slift"), S("Octagon", "18:30", "19:15", "Doodseskader"),
        S("Main B", "18:50", "19:45", "The Ghost Inside"), S("Obscure", "19:10", "20:00", "Vader"),
        S("Octagon", "19:45", "20:30", "Ragana"), S("Main A", "19:50", "20:55", "Candlemass"),
        S("Obscure", "20:30", "21:20", "Deafheaven"), S("Main B", "21:00", "22:00", "Left to Die"),
        S("Octagon", "21:00", "22:00", "The Ruins of Beverast"), S("Main A", "22:05", "23:20", "Wardruna"),
        S("Obscure", "22:00", "22:40", "Cryptopsy"), S("Octagon", "22:30", "23:30", "Der Weg Einer Freiheit"),
        S("Main B", "23:25", "00:25", "Satyricon"), S("Obscure", "23:20", "00:10", "Health"),
        S("Main A", "00:30", "01:30", "Perturbator"), S("Obscure", "00:40", "01:30", "Borknagar"),
        S("Obscure", "02:00", "02:40", "Carpathian Forest"),
        S("KAL", "18:30", "19:20", "Krolowczana Smuga"), S("KAL", "20:00", "20:40", "Kalich"),
        S("KAL", "21:20", "22:10", "Bunkerer"), S("KAL", "22:50", "23:40", "Zaffer 9"),
        S("KAL", "23:50", "01:00", "Trauma"), S("KAL", "01:00", "02:10", "C4AT"),
        S("KAL", "02:10", "03:30", "Madona")
    ];
}
