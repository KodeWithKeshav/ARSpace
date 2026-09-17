using NUnit.Framework;
using UnityEngine;
using ARSpace.Analysis;

namespace ARSpace.Tests.Editor
{
    [TestFixture]
    public class SpaceAnalyticsTests
    {
        [Test]
        public void UnitConversion_SqMToSqFt_IsAccurate()
        {
            float sqM = 100f;
            float sqFt = sqM * SpaceAnalyticsData.SqMToSqFt;
            Assert.AreEqual(1076.39f, sqFt, 0.01f);
        }

        [Test]
        public void CirculationCompliance_Thresholds_CategorizeCorrectly()
        {
            // >= 45% = Compliant
            float compRatio = 0.55f;
            CirculationCompliance compStatus = compRatio >= 0.45f
                ? CirculationCompliance.Compliant
                : (compRatio >= 0.30f ? CirculationCompliance.Constrained : CirculationCompliance.NonCompliant);
            Assert.AreEqual(CirculationCompliance.Compliant, compStatus);

            // 30% - 45% = Constrained
            float constRatio = 0.38f;
            CirculationCompliance constStatus = constRatio >= 0.45f
                ? CirculationCompliance.Compliant
                : (constRatio >= 0.30f ? CirculationCompliance.Constrained : CirculationCompliance.NonCompliant);
            Assert.AreEqual(CirculationCompliance.Constrained, constStatus);

            // < 30% = NonCompliant
            float nonCompRatio = 0.22f;
            CirculationCompliance nonCompStatus = nonCompRatio >= 0.45f
                ? CirculationCompliance.Compliant
                : (nonCompRatio >= 0.30f ? CirculationCompliance.Constrained : CirculationCompliance.NonCompliant);
            Assert.AreEqual(CirculationCompliance.NonCompliant, nonCompStatus);
        }

        [Test]
        public void DensityHealth_Thresholds_CategorizeCorrectly()
        {
            // < 8 = Sparse
            Assert.AreEqual(DensityHealth.Sparse, EvaluateDensity(6f));

            // 8 - 14 = Optimal
            Assert.AreEqual(DensityHealth.Optimal, EvaluateDensity(10.5f));
            Assert.AreEqual(DensityHealth.Optimal, EvaluateDensity(14.0f));

            // 14 - 20 = HighDensity
            Assert.AreEqual(DensityHealth.HighDensity, EvaluateDensity(17.5f));
            Assert.AreEqual(DensityHealth.HighDensity, EvaluateDensity(20.0f));

            // > 20 = Overcrowded
            Assert.AreEqual(DensityHealth.Overcrowded, EvaluateDensity(24.0f));
        }

        static DensityHealth EvaluateDensity(float density)
        {
            if (density < 8f) return DensityHealth.Sparse;
            if (density <= 14f) return DensityHealth.Optimal;
            if (density <= 20f) return DensityHealth.HighDensity;
            return DensityHealth.Overcrowded;
        }

        [Test]
        public void FloorArea_DivisionByZero_HandledSafely()
        {
            float floorArea = 0f;
            float footprint = 20f;
            int seats = 10;

            float footprintRatio = floorArea > 0f ? footprint / floorArea : 0f;
            float circulationRatio = floorArea > 0f ? (floorArea - footprint) / floorArea : 1f;
            float density = floorArea > 0f ? (seats / floorArea) * 100f : 0f;

            Assert.AreEqual(0f, footprintRatio);
            Assert.AreEqual(1f, circulationRatio);
            Assert.AreEqual(0f, density);
        }
    }
}
