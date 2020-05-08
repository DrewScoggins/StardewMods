using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using StardewValley;
using StardewValley.Menus;

namespace Pathoschild.Stardew.ChestsAnywhere.Framework.Containers
{

    public class ContainerSerialize : IXmlSerializable
    {
        public IContainer Container { get; set; }
        public XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(XmlReader reader)
        {
            reader.ReadStartElement("Conatiner");
            string strType = reader.GetAttribute("type");
            XmlSerializer serial = new XmlSerializer(Type.GetType(strType));
            this.Container = (IContainer)serial.Deserialize(reader);
            reader.ReadEndElement();
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("Container");
            string strType = this.Container.GetType().FullName;
            writer.WriteAttributeString("type", strType);
            XmlSerializer serial = new XmlSerializer(Type.GetType(strType));
            serial.Serialize(writer, this.Container);
            writer.WriteEndElement();
        }
    }
    /// <summary>An in-game container which can store items.</summary>
    public interface IContainer
    {
        /*********
        ** Accessors
        *********/
        /// <summary>The underlying inventory.</summary>
        IList<Item> Inventory { get; }

        /// <summary>The persisted data for this container.</summary>
        ContainerData Data { get; }

        /// <summary>Whether the player can customize the container data.</summary>
        bool IsDataEditable { get; }

        /// <summary>Whether Automate options can be configured for this chest.</summary>
        bool CanConfigureAutomate { get; }


        /*********
        ** Public methods
        *********/
        /// <summary>Get whether the inventory can accept the item type.</summary>
        /// <param name="item">The item.</param>
        bool CanAcceptItem(Item item);

        /// <summary>Get whether another instance wraps the same underlying container.</summary>
        /// <param name="container">The other container.</param>
        bool IsSameAs(IContainer container);

        /// <summary>Get whether another instance wraps the same underlying container.</summary>
        /// <param name="inventory">The other container's inventory.</param>
        bool IsSameAs(IList<Item> inventory);

        /// <summary>Open a menu to transfer items between the player's inventory and this container.</summary>
        IClickableMenu OpenMenu();

        /// <summary>Persist the container data.</summary>
        void SaveData();
    }
}
