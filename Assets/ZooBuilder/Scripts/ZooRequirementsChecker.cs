using System.Collections.Generic;
using UnityEngine;

namespace ZooBuilder
{
    /// <summary>
    /// Validates that all zoo requirements are met before the builder can save/exit.
    ///
    /// Requirements:
    ///   Global:
    ///     - Exactly 3 enclosures placed
    ///     - Path connects all 3 enclosures
    ///
    ///   Per enclosure:
    ///     - Fences + at least one gate fully surround the floor
    ///     - Animals: same type per enclosure, different types across enclosures
    ///     - Bin present outside the fence on the enclosure floor
    ///
    ///   Enclosure 1: >= 2 static animals
    ///   Enclosure 2: >= 2 roaming animals
    ///   Enclosure 3: >= 1 hungry animal
    /// </summary>
    public class ZooRequirementsChecker : MonoBehaviour
    {
        public static ZooRequirementsChecker Instance { get; private set; }

        [SerializeField] PathCreator m_PathCreator;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        // ── Public API ───────────────────────────────────────────────────────

        public RequirementResult CheckAll()
        {
            var result = new RequirementResult();

            if (ZooManager.Instance == null)
            {
                result.AddFailure("ZooManager not found.");
                return result;
            }

            CheckEnclosureCount(result);
            CheckPath(result);

            if (ZooManager.Instance.Enclosures.Count >= 3)
            {
                CheckAnimalTypes(result);
                foreach (var enc in ZooManager.Instance.Enclosures)
                    CheckEnclosure(enc, result);
            }

            return result;
        }

        // ── Individual checks ─────────────────────────────────────────────────

        void CheckEnclosureCount(RequirementResult r)
        {
            int count = ZooManager.Instance.Enclosures.Count;
            if (count < 3)
                r.AddFailure($"Need 3 enclosures. Currently have {count}.");
            else
                r.AddPass("3 enclosures placed.");
        }

        void CheckPath(RequirementResult r)
        {
            if (m_PathCreator == null || !m_PathCreator.PathFinalized)
            {
                r.AddFailure("Path not yet finalized.");
                return;
            }
            if (!m_PathCreator.AllEnclosuresConnected())
                r.AddFailure("Path does not connect all 3 enclosures.");
            else
                r.AddPass("Path connects all enclosures.");
        }

        void CheckAnimalTypes(RequirementResult r)
        {
            // Each enclosure must use a different animal model (prefabId)
            var typeByEnclosure = new Dictionary<EnclosureType, string>();

            foreach (var enc in ZooManager.Instance.Enclosures)
            {
                string animalPrefabId = GetAnimalPrefabId(enc);
                if (animalPrefabId == null)
                {
                    r.AddFailure($"{enc.EnclosureType}: no animals placed yet.");
                    continue;
                }

                // Check no two enclosures share the same animal prefab
                foreach (var kv in typeByEnclosure)
                {
                    if (kv.Value == animalPrefabId)
                    {
                        r.AddFailure($"{enc.EnclosureType} and {kv.Key} use the same animal model ('{animalPrefabId}'). " +
                                     "Each enclosure must have a different animal species.");
                    }
                }
                typeByEnclosure[enc.EnclosureType] = animalPrefabId;
            }

            if (typeByEnclosure.Count == 3 &&
                new HashSet<string>(typeByEnclosure.Values).Count == 3)
                r.AddPass("Each enclosure uses a different animal species.");
        }

        void CheckEnclosure(EnclosureFloor enc, RequirementResult r)
        {
            string encName = enc.EnclosureType.ToString();

            // Count object types
            int fenceCount = 0, gateCount = 0, binCount = 0, animalCount = 0;
            foreach (var obj in enc.PlacedObjects)
            {
                switch (obj.ObjectType)
                {
                    case ZooObjectType.Fence:  fenceCount++;  break;
                    case ZooObjectType.Gate:   gateCount++;   break;
                    case ZooObjectType.Bin:    binCount++;    break;
                    case ZooObjectType.Animal: animalCount++; break;
                }
            }

            // Fences present
            if (fenceCount == 0)
                r.AddFailure($"{encName}: No fence segments placed.");
            else
                r.AddPass($"{encName}: Has fence segments.");

            // At least one gate
            if (gateCount == 0)
                r.AddFailure($"{encName}: Must have at least one gate.");
            else
                r.AddPass($"{encName}: Has gate(s).");

            // Bin present
            if (binCount == 0)
                r.AddFailure($"{encName}: Must have at least one food bin (outside the fence).");
            else
                r.AddPass($"{encName}: Has food bin(s).");

            // Animal counts & behavior per enclosure type
            switch (enc.EnclosureType)
            {
                case EnclosureType.Enclosure1:
                    if (animalCount < 2)
                        r.AddFailure($"Enclosure 1: Needs at least 2 animals (has {animalCount}). Animals must be static.");
                    else
                        r.AddPass($"Enclosure 1: Has {animalCount} static animal(s).");
                    break;

                case EnclosureType.Enclosure2:
                    if (animalCount < 2)
                        r.AddFailure($"Enclosure 2: Needs at least 2 animals (has {animalCount}). Animals must roam randomly.");
                    else
                        r.AddPass($"Enclosure 2: Has {animalCount} roaming animal(s).");
                    break;

                case EnclosureType.Enclosure3:
                    if (animalCount < 1)
                        r.AddFailure($"Enclosure 3: Needs at least 1 hungry animal.");
                    else
                        r.AddPass($"Enclosure 3: Has {animalCount} hungry animal(s).");
                    break;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        string GetAnimalPrefabId(EnclosureFloor enc)
        {
            foreach (var obj in enc.PlacedObjects)
            {
                if (obj.ObjectType == ZooObjectType.Animal)
                    return obj.PrefabId;
            }
            return null;
        }

        // ── Convenience ──────────────────────────────────────────────────────

        public bool AllRequirementsMet() => CheckAll().AllPassed;
    }

    // ── Result container ──────────────────────────────────────────────────────

    public class RequirementResult
    {
        public List<string> Failures { get; } = new List<string>();
        public List<string> Passes   { get; } = new List<string>();

        public bool AllPassed => Failures.Count == 0;

        public void AddFailure(string msg) => Failures.Add(msg);
        public void AddPass(string msg)    => Passes.Add(msg);

        public string Summary()
        {
            if (AllPassed) return "All requirements met!";
            return string.Join("\n", Failures);
        }
    }
}
