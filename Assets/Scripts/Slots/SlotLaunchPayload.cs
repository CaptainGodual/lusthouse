using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Hands the chosen definition to the shared slot scene across the load.
    ///
    /// A static payload is the simplest thing that survives <c>LoadScene</c>; the
    /// alternative is a third scene, which plan §5.2 forbids.
    /// </summary>
    public static class SlotLaunchPayload
    {
        public static SlotMachineDefinition Payload;
    }
}