using FLT.IO;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace FLT
{
    public class FamilyLibraryToolsCore : MonoBehaviour
    {
        static FamilyLibraryToolsCore fltCore;
        public static FamilyLibraryToolsCore FLTCore { get => fltCore; }

        FLTReader reader;

        public UnityEvent OnBaseFolderPathUpdate;
        [ShowInInspector] string baseFolderPath;

        // Start is called before the first frame update
        void Start()
        {
            reader = new FLTReader();
        }

        // Update is called once per frame
        void Update()
        {

        }

        public void SetFolderPath()
        {
            if (reader.PickFolder(out var newFolderPath))
            {
                baseFolderPath = newFolderPath;
                OnBaseFolderPathUpdate?.Invoke();
            }
        }

        public void Analyze()
        {

        }
    }
}