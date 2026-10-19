#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using LustHouse.Slots;

namespace LustHouse.EditorTools
{
    public class MathSimulator : EditorWindow
    {
        private SlotMachineDefinition _def;
        private int _spins = 100000;

        [MenuItem("LustHouse/Math Simulator")]
        public static void Open() => GetWindow<MathSimulator>("Math Simulator");

        private void OnGUI()
        {
            _def = (SlotMachineDefinition)EditorGUILayout.ObjectField("Definition", _def, typeof(SlotMachineDefinition), false);
            _spins = EditorGUILayout.IntField("Spins", _spins);
            if (GUILayout.Button("Run") && _def != null)
                Debug.Log($"Simulating {_spins} spins of {_def.id} (stub).");
        }
    }
}
#endif