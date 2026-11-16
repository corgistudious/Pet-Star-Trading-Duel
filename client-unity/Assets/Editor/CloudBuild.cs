using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PetStar.EditorTools
{
    /// <summary>
    /// Unity Build Automation (Unity Cloud Build) entry point. In the build target's Advanced settings set
    ///   Pre-Export Method Name: PetStar.EditorTools.CloudBuild.PreExport
    /// It runs inside the cloud Editor after the project is imported and before the player is built:
    /// configures sprites/Addressables/fonts/scene/player settings, validates the project (fails the build with a
    /// clear list on errors), selects the client environment and builds Addressables content.
    /// No license files, serials or keystores are needed: Build Automation provides the Editor license itself.
    /// Optional environment variables (Build Automation > Environment variables): PETSTAR_ENV, PETSTAR_API_BASE,
    /// PETSTAR_VERSION.
    /// </summary>
    public static class CloudBuild
    {
        public static void PreExport()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            PetStarBuild.Log($"Build Automation pre-export: target {target}, Unity {Application.unityVersion}");
            PetStarBuild.Log("project path " + Path.GetDirectoryName(Application.dataPath));
            PetStarBuild.Log("environment PETSTAR_ENV=" + (Environment.GetEnvironmentVariable("PETSTAR_ENV") ?? "(default development)")
                + ", PETSTAR_API_BASE=" + (Environment.GetEnvironmentVariable("PETSTAR_API_BASE") ?? "(not set)"));
            if (target != BuildTarget.WebGL)
                PetStarBuild.Warn($"target {target}: WebGL is the configured priority; other targets are deferred");

            PetStarBuild.Setup(target);
            PetStarBuild.SetEnvironment();
            ProjectValidator.RunOrThrow(target);
            // Addressables content must exist before the player is built. If the Build Automation
            // "Addressables" option is also enabled, the content is simply rebuilt (harmless).
            PetStarBuild.BuildAddressables();
            PetStarBuild.Log("pre-export finished; Build Automation now builds the player");
        }
    }
}
