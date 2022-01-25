// [TEN Code Block Start]
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Nekoyume.Game.TenScriptableObject
{
    [Serializable]
    public class Block
    {
        [SerializeField] private int index;
        
        public int Index => index;
    }

    [Serializable]
    public class Response
    {
        [SerializeField] private List<Block> blocks;
        
        public List<Block> Blocks => blocks;
    }
}
// [TEN Code Block End]