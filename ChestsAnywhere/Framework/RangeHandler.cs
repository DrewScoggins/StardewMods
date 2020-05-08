using System;
using System.Collections.Generic;
using System.Linq;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;

namespace Pathoschild.Stardew.ChestsAnywhere.Framework
{
    /// <summary>Determines whether given locations are in range of the player for remote chest access.</summary>
    internal class RangeHandler
    {
        /*********
        ** Fields
        *********/
        /// <summary>The range within which chests should be accessible.</summary>
        private readonly ChestRange Range;

        /// <summary>The player's current zone.</summary>
        private readonly string CurrentZone;

        /// <summary>A location => zone lookup if <see cref="Range"/> is <see cref="ChestRange.CurrentWorldArea"/>.</summary>
        private readonly IDictionary<string, string> WorldAreaZones;


        /*********
        ** Public methods
        *********/
        /// <summary>Construct an instance.</summary>
        /// <param name="worldAreas">The predefined world areas for <see cref="ChestRange.CurrentWorldArea"/>.</param>
        /// <param name="range">The range within which chests should be accessible.</param>
        /// <param name="currentLocation">The player's current location.</param>
        public RangeHandler(IDictionary<string, HashSet<string>> worldAreas, ChestRange range, GameLocation currentLocation)
        {
            this.Range = range;

            if (range == ChestRange.CurrentWorldArea)
                this.WorldAreaZones = this.GetWorldAreaZones(worldAreas);
            bool isMine = currentLocation is MineShaft;
            bool isSkullMine = currentLocation is MineShaft ? ((MineShaft)currentLocation).mineLevel > 120 ? true : false : false;
            this.CurrentZone = this.GetZone(currentLocation.Name, range, isMine, isSkullMine);
        }

        /// <summary>Get whether a location is within range of the player.</summary>
        /// <param name="location">The location to check.</param>
        public bool IsInRange(string location, bool isMine, bool isSkullMine)
        {
            string zone = this.GetZone(location, this.Range, isMine, isSkullMine);
            return zone != null && zone == this.CurrentZone;
        }

        /// <summary>Get a range handler which doesn't restrict the range.</summary>
        public static RangeHandler Unlimited()
        {
            return new RangeHandler(null, ChestRange.Unlimited, Game1.currentLocation);
        }

        /// <summary>Get a range handler which restricts access to the current location.</summary>
        public static RangeHandler CurrentLocation()
        {
            return new RangeHandler(null, ChestRange.CurrentLocation, Game1.currentLocation);
        }


        /*********
        ** Private methods
        *********/
        /// <summary>Get the zone key for a location.</summary>
        /// <param name="location">The location to check.</param>
        /// <param name="range">The range within which chests should be accessible.</param>
        private string GetZone(string location, ChestRange range, bool isMine, bool isSkullMine)
        {
            switch (range)
            {
                case ChestRange.Unlimited:
                    return "*";

                case ChestRange.CurrentWorldArea:
                    if (isMine)
                        return isSkullMine ? "SkullCave" : "Mine"; // match entrance name

                    return this.WorldAreaZones.TryGetValue(location, out string zone)
                        ? zone
                        : location;

                case ChestRange.CurrentLocation:
                    return location;

                case ChestRange.None:
                    return null;

                default:
                    throw new NotSupportedException($"Unknown range '{range}'.");
            }
        }

        /// <summary>Get a lookup which matches locations to world area zones.</summary>
        /// <param name="worldAreas">The predefined world areas for <see cref="ChestRange.CurrentWorldArea"/>.</param>
        private IDictionary<string, string> GetWorldAreaZones(IDictionary<string, HashSet<string>> worldAreas)
        {
            IDictionary<string, string> zones = new Dictionary<string, string>();

            foreach (GameLocation location in Game1.locations)
            {
                // get zone key
                string zone = (from area in worldAreas where area.Value.Contains(location.Name) select area.Key).FirstOrDefault()
                    ?? location.Name;

                // add location + buildings
                zones[location.Name] = zone;
                if (location is BuildableGameLocation buildableLocation)
                {
                    foreach (Building building in buildableLocation.buildings)
                    {
                        GameLocation indoors = building.indoors.Value;
                        if (indoors != null)
                            zones[indoors.Name] = zone;
                    }
                }
            }

            return zones;
        }
    }
}
