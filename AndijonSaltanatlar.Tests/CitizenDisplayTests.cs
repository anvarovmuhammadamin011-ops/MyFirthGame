using AndijonSaltanatlar.Core.Graphics;
using AndijonSaltanatlar.Units;
using Microsoft.Xna.Framework;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    public sealed class CitizenDisplayTests
    {
        [Theory]
        [InlineData(CitizenArt.Citizen, "ODDIY ODAM")]
        [InlineData(CitizenArt.Merchant, "SAVDOGAR")]
        [InlineData(CitizenArt.Craftsman, "HUNARMAND")]
        [InlineData(CitizenArt.Farmer, "DEHQON")]
        [InlineData(CitizenArt.Buyer, "XARIDOR")]
        [InlineData(CitizenArt.Shepherd, "CHOPON")]
        [InlineData(CitizenArt.Miner, "KONCHI")]
        [InlineData(CitizenArt.Scholar, "OLIM")]
        [InlineData(CitizenArt.Caravaneer, "KARVONCHI")]
        [InlineData(CitizenArt.Kinds, "ODDIY ODAM")]
        public void CitizenRoleLabelMatchesCitizenKind(int kind, string expectedLabel)
        {
            var citizen = new CitizenUnit(new[] { Vector2.Zero, Vector2.One }, kind);

            Assert.Equal(expectedLabel, citizen.RoleLabel);
        }

        [Fact]
        public void CitizenCanWorkOnlyOncePerGameDay()
        {
            var citizen = new CitizenUnit(new[] { Vector2.Zero, Vector2.One }, CitizenArt.Farmer);

            Assert.True(citizen.TryBeginWork(1));
            Assert.False(citizen.TryBeginWork(1));
            Assert.True(citizen.TryBeginWork(2));
        }
    }
}
