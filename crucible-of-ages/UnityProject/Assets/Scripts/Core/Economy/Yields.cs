using System;

namespace Crucible.Core.Economy
{
    /// <summary>The Civ V yield bundle produced by tiles, buildings and cities.</summary>
    [Serializable]
    public struct Yields : IEquatable<Yields>
    {
        public int Food;
        public int Production;
        public int Gold;
        public int Science;
        public int Culture;
        public int Faith;

        public Yields(int food = 0, int production = 0, int gold = 0, int science = 0, int culture = 0, int faith = 0)
        {
            Food = food;
            Production = production;
            Gold = gold;
            Science = science;
            Culture = culture;
            Faith = faith;
        }

        public static Yields operator +(Yields a, Yields b) => new Yields(
            a.Food + b.Food, a.Production + b.Production, a.Gold + b.Gold,
            a.Science + b.Science, a.Culture + b.Culture, a.Faith + b.Faith);

        public bool Equals(Yields o) =>
            Food == o.Food && Production == o.Production && Gold == o.Gold &&
            Science == o.Science && Culture == o.Culture && Faith == o.Faith;

        public override bool Equals(object obj) => obj is Yields y && Equals(y);
        public override int GetHashCode() => (Food, Production, Gold, Science, Culture, Faith).GetHashCode();

        public override string ToString() =>
            $"{Food}F {Production}P {Gold}G {Science}S {Culture}C" + (Faith != 0 ? $" {Faith}Fa" : "");
    }
}
