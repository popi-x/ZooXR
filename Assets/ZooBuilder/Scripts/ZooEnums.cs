namespace ZooBuilder
{
    public enum BuildPhase
    {
        DetectingPlane,
        PlacingEnclosures,
        CreatingPath,
        PlacingObjects,
        Complete
    }

    public enum EnclosureType
    {
        None = 0,
        Enclosure1 = 1,  // Animals stand still
        Enclosure2 = 2,  // Animals roam randomly
        Enclosure3 = 3   // Hungry animal
    }

    public enum ZooObjectType
    {
        Fence,
        Gate,
        Bin,
        Animal
    }

    public enum AnimalBehaviorType
    {
        Static,   // Enclosure 1 - stand still
        Roaming,  // Enclosure 2 - random roaming
        Hungry    // Enclosure 3 - aggressive
    }

    public enum PlacementMode
    {
        None,
        EnclosureFloor,
        Fence,
        Gate,
        Bin,
        Animal,
        Path
    }
}
