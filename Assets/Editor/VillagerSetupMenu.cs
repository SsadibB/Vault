using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Vault.AI;

namespace Vault.Editor
{
    public static class VillagerSetupMenu
    {
        private const string ScenePath = "Assets/AI_Villager/Ai_Villager_Scene.unity";

        [MenuItem("Vault/Setup AI Villager Scene")]
        public static string SetupAiVillagerScene()
        {
            // 1. Open or verify active scene
            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path != ScenePath)
            {
                activeScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var rootObjects = activeScene.GetRootGameObjects();

            // 2. Setup NavMeshSurface on Terrain
            GameObject terrainGo = GameObject.Find("Terrain");
            if (terrainGo == null)
            {
                return "Error: Terrain object not found in scene.";
            }

            NavMeshSurface surface = terrainGo.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = terrainGo.AddComponent<NavMeshSurface>();
            }

            // Bake NavMesh
            surface.BuildNavMesh();

            // 3. Setup NavMeshObstacle on Obstacle Cubes
            GameObject cube1 = GameObject.Find("Cube");
            if (cube1 != null && cube1.GetComponent<NavMeshObstacle>() == null)
            {
                var obs = cube1.AddComponent<NavMeshObstacle>();
                obs.carving = true;
            }

            GameObject cube2 = GameObject.Find("Cube (1)");
            if (cube2 != null && cube2.GetComponent<NavMeshObstacle>() == null)
            {
                var obs = cube2.AddComponent<NavMeshObstacle>();
                obs.carving = true;
            }

            // 4. Setup AI_Villager_Girl
            GameObject villagerGo = GameObject.Find("AI_Villager_Girl");
            if (villagerGo == null)
            {
                return "Error: AI_Villager_Girl object not found in scene.";
            }

            // Add or configure NavMeshAgent
            NavMeshAgent agent = villagerGo.GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = villagerGo.AddComponent<NavMeshAgent>();
            }

            agent.speed = 2.0f;
            agent.angularSpeed = 360f;
            agent.acceleration = 8.0f;
            agent.stoppingDistance = 0.5f;
            agent.radius = 0.4f;
            agent.height = 1.8f;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;

            // Add or configure VillagerAI
            VillagerAI villagerAI = villagerGo.GetComponent<VillagerAI>();
            if (villagerAI == null)
            {
                villagerAI = villagerGo.AddComponent<VillagerAI>();
            }

            // Ensure Animator is present and has controller + avatar
            Animator animator = villagerGo.GetComponent<Animator>();
            if (animator == null)
            {
                animator = villagerGo.AddComponent<Animator>();
            }

            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/AI_Villager/AI_Villager.controller");
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
            }

            // Instantiate FBX model as child if not present
            var modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/AI_Villager/Peasant Girl@Breathing Idle.fbx");
            if (modelPrefab != null)
            {
                Transform childModel = villagerGo.transform.Find("PeasantGirlModel");
                if (childModel == null)
                {
                    GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelPrefab, villagerGo.transform);
                    if (modelInstance != null)
                    {
                        modelInstance.name = "PeasantGirlModel";
                        modelInstance.transform.localPosition = Vector3.zero;
                        modelInstance.transform.localRotation = Quaternion.identity;
                        modelInstance.transform.localScale = Vector3.one;
                    }
                }
            }

            // Assign Avatar from model importer
            var representations = AssetDatabase.LoadAllAssetsAtPath("Assets/AI_Villager/Peasant Girl@Breathing Idle.fbx");
            foreach (var asset in representations)
            {
                if (asset is Avatar modelAvatar)
                {
                    animator.avatar = modelAvatar;
                    break;
                }
            }

            EditorUtility.SetDirty(villagerGo);
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[VillagerSetupMenu] Successfully configured AI Villager scene with NavMesh, NavMeshAgent, and NavMeshObstacles.");
            return "OK: AI Villager scene configured and NavMesh baked successfully.";
        }
    }
}
