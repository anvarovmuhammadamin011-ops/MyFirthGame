using System.Linq;
using System.Reflection;
using AndijonSaltanatlar.Server.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AndijonSaltanatlar.Tests
{
    /// <summary>Bulutga saqlash: katta (rivojlangan) saltanat saqlanishi server chegarasidan oshib ketmasin.</summary>
    public sealed class SaveLimitTests
    {
        [Fact]
        public void CloudSaveLimit_FitsLargeKingdomSaves()
        {
            // Haqiqiy o'yinchi saqlanishi ~9 MB ga yetgan; chegara uni zaxira bilan sig'dirishi kerak
            Assert.True(SaveController.MaxBytes >= 32 * 1024 * 1024);
            Assert.NotNull(typeof(SaveController).GetCustomAttributes<RequestSizeLimitAttribute>().FirstOrDefault());
        }
    }
}
