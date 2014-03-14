namespace WebRole1
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    public class Node
    {
        private Node[] children;
        protected byte data; // use the left-most bit to indicate whether this node is a result

        /// <summary>
        /// Default constructor
        /// </summary>
        public Node()
        {
            children = new Node[0];
            data = 0;
        }

        public byte Data
        {
            get
            {
                return (byte)(data & 0x7F); // binary 0111 1111
            }
        }

        public bool IsResult
        {
            get
            {
                byte b = 0x80; // binary 1000 0000
                return (b == (data & b));
            }
            set
            {
                if (value == true)
                {
                    data |= 0x80; // binary 1000 0000
                }
                else
                {
                    data &= 0x7F; // binary 0111 1111
                }
            }
        }

        /// <summary>
        /// Return whether this is a terminal node
        /// (i.e. no child nodes)
        /// </summary>
        public bool IsTerminal
        {
            get
            {
                return (children.Length == 0);
            }
        }

        public Node[] Children
        {
            get
            {
                return children;
            }
        }

        /// <summary>
        /// Insert a child node
        /// </summary>
        /// <param name="child">Child node</param>
        public Node InsertChild(byte key)
        {
            Node child = new Node();

            // Put key to the child's data
            child.data = key;

            // Add child node to this node's array
            Array.Resize<Node>(ref children, children.Length + 1);

            // Insert child in assending order
            children[children.Length - 1] = child;
            for (int i = children.Length - 1; i > 0; i--)
            {
                if (children[i].data < children[i - 1].data)
                {
                    Node temp = children[i];
                    children[i] = children[i - 1];
                    children[i - 1] = temp;
                }
                else
                {
                    break;
                }
            }

            return child;
        }

        public Node InsertChild(char key)
        {
            return this.InsertChild((byte)key);
        }

        public bool ContainsKey(byte key)
        {
            foreach(Node node in children)
            {
                if (key == (byte)(node.data & 0x7F)) // binary 0111 1111
                {
                    return true;
                }
            }
            return false;
        }

        public bool ContainsKey(char key)
        {
            return this.ContainsKey((byte)key);
        }

        public Node GetChildForKey(byte key)
        {
            foreach (Node node in children)
            {
                if (key == (byte)(node.data & 0x7F)) // binary 0111 1111
                {
                    return node;
                }
            }
            return null;
        }

        public Node GetChildForKey(char key)
        {
            return this.GetChildForKey((byte)key);
        }
    }
}