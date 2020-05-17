using System;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace Pathoschild.Stardew.ChestsAnywhere.Framework
{
    public class PsuedoGameLocation
    {
        public string MineName { get; set; }
        public bool MineShaft { get; set; }
        public string Name { get; set; }

        public PsuedoGameLocation(GameLocation gl)
        {
            if(gl is MineShaft mine)
            {
                this.MineShaft = true;
                this.MineName = mine.mineLevel <= 120 ? "Mine" : "SkullCave";
            }
            this.Name = gl.Name;
        }

        public PsuedoGameLocation()
        {

        }

        public override int GetHashCode()
        {
            return this.MineName != null ? this.MineName.GetHashCode() : 0 + this.MineShaft.GetHashCode() + this.Name.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if(obj is PsuedoGameLocation pLoc)
            {
                if(pLoc?.MineName == this?.MineName && pLoc.MineShaft == pLoc.MineShaft && pLoc.Name == this.Name)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
    }

    public class ChestPsuedoGameLocation
    {
        public Chest Chest { get; set; }
        public PsuedoGameLocation PLoc { get; set; }

        public ChestPsuedoGameLocation(Chest chest, PsuedoGameLocation pLoc)
        {
            this.Chest = chest;
            this.PLoc = pLoc;
        }

        public ChestPsuedoGameLocation()
        {

        }
    }
}
