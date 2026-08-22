using NUnit.Framework;
using ShitDesigner.Bootstrap;

namespace ShitDesigner.Bootstrap.Tests
{
    [TestFixture]
    public sealed class ProductionWindowConstraintTests
    {
        [Test]
        public void MinimumWindowContractClampsBothDimensions()
        {
            var clamped = ProductionWindowConstraints.Clamp(new ProductionWindowSize(900, 480));
            Assert.That(clamped, Is.EqualTo(new ProductionWindowSize(1280, 720)));
            Assert.That(ProductionWindowConstraints.NeedsClamp(clamped), Is.False);
        }

        [Test]
        public void PlatformAdapterIsAppliedAgainAfterUserResize()
        {
            var adapter = new RecordingWindowAdapter(new ProductionWindowSize(1600, 900));
            Assert.That(ProductionWindowConstraints.NeedsClamp(adapter.CurrentSize), Is.False);

            adapter.ResizeFromUser(new ProductionWindowSize(1024, 640));
            var corrected = ProductionWindowConstraints.Clamp(adapter.CurrentSize);
            adapter.SetWindowedSize(corrected);

            Assert.That(adapter.CurrentSize, Is.EqualTo(new ProductionWindowSize(1280, 720)));
            Assert.That(adapter.SetCount, Is.EqualTo(1));
            Assert.That(adapter.LastSetSize, Is.EqualTo(new ProductionWindowSize(1280, 720)));
        }

        [Test]
        public void InitialPlayerWindowContractIs1600By900()
        {
            Assert.That(ProductionWindowConstraints.InitialWidth, Is.EqualTo(1600));
            Assert.That(ProductionWindowConstraints.InitialHeight, Is.EqualTo(900));
            Assert.That(ProductionWindowConstraints.MinimumWidth, Is.EqualTo(1280));
            Assert.That(ProductionWindowConstraints.MinimumHeight, Is.EqualTo(720));
        }

        private sealed class RecordingWindowAdapter : IProductionWindowAdapter
        {
            public bool IsSupported => true;
            public bool IsWindowed => true;
            public ProductionWindowSize CurrentSize { get; private set; }
            public int SetCount { get; private set; }
            public ProductionWindowSize LastSetSize { get; private set; }

            public RecordingWindowAdapter(ProductionWindowSize size) { CurrentSize = size; }
            public void ResizeFromUser(ProductionWindowSize size) { CurrentSize = size; }
            public void SetWindowedSize(ProductionWindowSize size)
            {
                LastSetSize = size;
                CurrentSize = size;
                SetCount++;
            }
        }
    }
}
