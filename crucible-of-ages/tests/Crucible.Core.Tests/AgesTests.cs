using System;
using System.Linq;
using Crucible.Core.Content;
using Crucible.Core.Empire;
using Xunit;

namespace Crucible.Core.Tests
{
    /// <summary>The ten ages from the Neolithic to the Future era.</summary>
    public class AgesTests
    {
        static readonly ContentDatabase Content = TestWorld.Content;

        [Fact]
        public void Every_age_has_research_and_land_troops_of_its_own()
        {
            foreach (Era era in Enum.GetValues(typeof(Era)))
            {
                Assert.Contains(Content.Techs, t => t.Era == era);
                Assert.Contains(Content.Units, u => u.Era == era && u.IsMilitary && u.Domain == UnitDomain.Land && u.GreatPerson == GreatPersonType.None);
            }
            Assert.Equal(Era.Neolithic, Enum.GetValues(typeof(Era)).Cast<Era>().Min());
            Assert.Equal(Era.Future, Enum.GetValues(typeof(Era)).Cast<Era>().Max());
        }

        [Fact]
        public void The_neolithic_opens_the_tree_and_every_tech_builds_on_its_own_age_or_earlier()
        {
            var roots = Content.Techs.Where(t => t.Prerequisites.Count == 0).ToList();
            Assert.NotEmpty(roots);
            Assert.All(roots, t => Assert.Equal(Era.Neolithic, t.Era));             // nobody skips the Neolithic
            Assert.Equal(new[] { "animism", "hunting", "stone_tools" }, roots.Select(t => t.Id).OrderBy(id => id));
            // Every Neolithic tech leads somewhere, and every later tech needs one of them somewhere up its tree.
            Assert.All(roots, r => Assert.Contains(Content.Techs, t => t.Prerequisites.Contains(r.Id)));
            var tree = new TechTree(Content);
            Assert.All(Content.Techs.Where(t => t.Era > Era.Neolithic), t => Assert.Contains(tree.PathTo(t.Id), id => Content.Tech(id).Era == Era.Neolithic));
            foreach (var tech in Content.Techs)
                foreach (var pre in tech.Prerequisites)
                    Assert.True(Content.Tech(pre).Era <= tech.Era, $"{tech.Id} needs later {pre}");
            // Units and buildings unlock in their own age or later, never before it.
            foreach (var unit in Content.Units.Where(u => u.RequiredTech != null))
                Assert.True(Content.Tech(unit.RequiredTech).Era <= unit.Era, unit.Id);
        }

        [Fact]
        public void A_civilisation_advances_through_the_ages_as_it_researches()
        {
            var tree = new TechTree(Content);
            Assert.Equal(Era.Neolithic, tree.CurrentEra);
            tree.Grant("hunting");
            Assert.Equal(Era.Neolithic, tree.CurrentEra);
            Assert.Equal(new[] { "stone_tools", "agriculture" }, tree.PathTo("agriculture"));
            tree.Grant("stone_tools");
            tree.Grant("agriculture");
            Assert.Equal(Era.Ancient, tree.CurrentEra);
            foreach (var id in tree.PathTo("predictive_systems")) tree.Grant(id);
            Assert.Equal(Era.Future, tree.CurrentEra);
            Assert.True(tree.Has(Content.Unit("giant_death_robot").RequiredTech));
            Assert.True(tree.Has(Content.Unit("exosuit_infantry").RequiredTech));
        }
    }
}
