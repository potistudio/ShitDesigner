using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Persistence;
using UnityEngine.TestTools;

namespace ShitDesigner.Application.Tests.PlayMode
{
    [TestFixture]
    public sealed class ProjectApplicationPlayModeTests
    {
        [UnityTest]
        public IEnumerator FramePumpPublishesApplicationSnapshot()
        {
            var root = Path.Combine(Path.GetTempPath(), "ShitDesigner-Play-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var application = new ProjectApplication(new LocalProjectFileSystem()))
                {
                    Assert.That(application.NewProject("Play", Path.Combine(root, "Project")).IsSuccess, Is.True);
                    yield return null;
                    var report = application.Tick(0d);
                    Assert.That(report, Is.Not.Null);
                    Assert.That(application.ReadModel.Project.IsFullSnapshot, Is.True);
                    Assert.That(application.ReadModel.Project.FrameNumber, Is.EqualTo(1));
                }
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
