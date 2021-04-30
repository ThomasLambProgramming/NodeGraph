﻿using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Analytics;
//NOTES FOR WHAT TO ADD OR REMOVE LATER
//Include option for objects to be selected for normal or for y axis checks
//get the object point (hopefully middle or bottom and do a direction dot product check for if the node will be above or below to remove verts


/*
    Optimization ideas
    sort the nodes by position (since the node graph has random access knowing the bounds of the x,z,y we can use binary search for the finding of closest nodes)

    dot product for finding angles on objects instead of normal / square check

    possible griding of the nodegraph into sections with x,z bounds to tell if it needs to search the whole graph or not (can also use binary search as well here for finding grid distances and etc)

*/
public class Node
{
    //by making a preset i can make these nodes into an array 
    public Node[] m_connectedNodes = null;
    public Vector3 m_position = new Vector3(0,0,0);
    public int m_connectionAmount = 0;
    //this is the nodes normal for checking if other nodes need to be deleted
    public Vector3 m_normal = new Vector3(0, 0, 0);
    public Node(int a_nodeConnectionLimit, Vector3 a_position, Vector3 a_normal)
    {
        m_position = a_position;
        m_connectedNodes = new Node[a_nodeConnectionLimit];
        m_normal = a_normal;
        m_connectionAmount = 0;
    }
}




public class NodeManager : MonoBehaviour
{
    private static ComputeShader m_NodeLinkingShader = null;
    
    //Static variables for use by the whole system
    private static float m_nodeDistance = 5;
    private static int m_nodeConnectionAmount = 4;
    private static int m_maxNodes = 1000;
    private static float m_ySpaceLimit = 1;

    //this is the result of baking and linking the nodes
    //This is an array so the memory is compact and can be randomly accessed by the 
    //pathfinding algorithm so for loops can be avoided
    public static Node[] m_nodeGraph = null;

    //list of all the node positions for the linking process
    private static List<Node> m_createdNodes = new List<Node>();

    //list of all the object positions so that the system knows what objects have already 
    //been processed (this does imply no objects are allowed to overlap perfectly)
    private static List<Vector3> m_objectPositions = new List<Vector3>();

    public void GetShader()
    {
        GameObject[] finder = FindObjectsOfType<GameObject>();
        foreach (var obj in finder)
        {
            if (finder != null)
            {
                if (obj.CompareTag("ShaderHolder"))
                {
                    var temp = obj.GetComponent<ShaderHolder>();
                    m_NodeLinkingShader = temp.shader;
                }
            }
        }
    }
    //debug purposes
    public static void ResetValues()
    {
        m_createdNodes = new List<Node>();
        m_objectPositions = new List<Vector3>();
        m_nodeGraph = null;
    }
    public static void ChangeValues(float a_nodeDistance, int a_connectionAmount, int a_maxNodes, float a_yLimit)
    {
        m_nodeDistance = a_nodeDistance;
        m_nodeConnectionAmount = a_connectionAmount;
        m_maxNodes = a_maxNodes;
        m_ySpaceLimit = a_yLimit;
    }
    public static void CreateNodes(int a_layerMask)
    {
        //gets every gameobject (change later to be a selection of some sort, possibly layers or manual selection   
        GameObject[] foundObjects = FindObjectsOfType<GameObject>();


        List<Vector3> nodePositions = new List<Vector3>();
        List<Vector3> normalPositions = new List<Vector3>();

        foreach (GameObject currentObject in foundObjects)
        {

            //if its a node (debug currently creates spheres) goto next object
            if (currentObject.CompareTag("Node"))
                continue;

            //if the object doesnt have a mesh next
            MeshFilter objectMesh = currentObject.GetComponent<MeshFilter>();
            if (objectMesh == null)
                continue;

            //if the object has already been processed next
            if (m_objectPositions.Contains(currentObject.transform.position))
                continue;

            Vector3 newNormal = currentObject.transform.TransformDirection(new Vector3(0, 1, 0));

            List<Vector3> objectVerts = new List<Vector3>();
            foreach (var vert in objectMesh.sharedMesh.vertices)
            {
                if (objectVerts.Contains(vert))
                    continue;

                //gets the scale relative to the current object (the cube can have a scale of 20 so the position in world is larger)
                Vector3 vertScale = new Vector3(
                    vert.x * currentObject.transform.localScale.x,
                    vert.y * currentObject.transform.localScale.y,
                    vert.z * currentObject.transform.localScale.z);
                Vector3 vertWorldPos = currentObject.transform.TransformPoint(vertScale);
                bool canAdd = true;
                for (int i = 0; i < nodePositions.Count; i++)
                {

                    float dist = Vector3.Distance(vertWorldPos, nodePositions[i]);
                    Vector3 checkPosition = nodePositions[i] - newNormal * dist;
                    if (Vector3.Distance(checkPosition, vertWorldPos) < 0.3f)
                    {
                        float ydistance = vertWorldPos.y - nodePositions[i].y;
                        if (ydistance < 0)
                            canAdd = false;
                        else if (ydistance > m_ySpaceLimit)
                        {
                            canAdd = true;
                        }
                        else
                        {
                            nodePositions.Remove(nodePositions[i]);
                            normalPositions.RemoveAt(i);
                            i--;
                        }
                    }
                }
                //this gets the transformed 0,1 vector so i know what direction the y axis of the object is

                if (canAdd)
                {

                    //add the vert to check for overlaps
                    nodePositions.Add(vertWorldPos);
                    normalPositions.Add(newNormal);
                }


            }
            //add the checked object for later to stop double baking
            m_objectPositions.Add(currentObject.transform.position);
        


        }

        for (int i = 0; i < nodePositions.Count; i++)
        {

            m_createdNodes.Add(new Node(m_nodeConnectionAmount, nodePositions[i], normalPositions[i]));
        }
        //foreach list make new nodes

        Debug.Log("CREATION PASSED");
    }

    
    //Im going to have to change this to use the dot product so its not doing as many distance checks
    public static void LinkNodes(float a_nodeDistance, bool a_firstRun = true)
    {
        if (m_createdNodes == null)
            return;

        

        //loop each node over the whole collection for joining and deleting as needed
        //this needs to be changed to the graphics system soon instead of cpu loop
        foreach (var node1 in m_createdNodes)
        {
            foreach (var node2 in m_createdNodes)
            {
                //if same node goto next
                if (node1 == node2)
                    continue;
                
                //Double up check, checks both current node's connections to see if they are already linked
                bool hasDupe = false;
                foreach (var VARIABLE in node2.m_connectedNodes)
                {
                    if (VARIABLE == null)
                        continue;
                    if (VARIABLE == node1)
                        hasDupe = true;
                }
                foreach (var VARIABLE in node1.m_connectedNodes)
                {
                    if (VARIABLE == null)
                        continue;
                    if (VARIABLE == node2)
                        hasDupe = true;
                }
                if (hasDupe)
                    continue;

                //if distance between nodes less than set distance then we can add
                //add a_nodeDistance tothe distance check when done
                if (Vector3.Distance(node1.m_position, node2.m_position) > 1.3 && node1.m_position.y - node2.m_position.y == 0)
                    continue; 

                if (Vector3.Distance(node1.m_position, node2.m_position) < a_nodeDistance)
                {
                    //if the nodes have a spare slot then add eachother (for making it easy nodes have a current index
                    //used amount so its easy to tell if all the connection amounts are full
                    if (node1.m_connectionAmount < m_nodeConnectionAmount &&
                        node2.m_connectionAmount < m_nodeConnectionAmount)
                    {
                        //checks to see what part of the array is null so we dont overwrite or add to something that doesnt have space.
                        for (int i = 0; i < m_nodeConnectionAmount; i++)
                        {
                            if (node1.m_connectedNodes[i] == null)
                            {
                                //im making the weight for now have a heigher weight based on how much 
                                node1.m_connectedNodes[i] = node2;
                                node1.m_connectionAmount++;
                                break;
                            }
                        }

                        for (int i = 0; i < m_nodeConnectionAmount; i++)
                        {
                            if (node2.m_connectedNodes[i] == null)
                            {
                                node2.m_connectedNodes[i] = node1;
                                node2.m_connectionAmount++;
                                break;
                            }
                        }
                    }
                }
            }
        }
        
        
        m_nodeGraph = new Node[m_createdNodes.Count];

        //add all nodes into the array
        //This is not efficent to recreate entire containers but having the ability to random access
        //nodes is a big performance increase (especially with the compute shaders) 
        for (int i = 0; i < m_createdNodes.Count; i++)
        {
            m_nodeGraph[i] = m_createdNodes[i];
        }
        Debug.Log("LINK PASSED");
    }
    //This is a debug option
    public static void DrawNodes()
    {
        if (m_nodeGraph == null)
            return;

        foreach (var node in m_nodeGraph)
        {
            for(int i = 0; i < node.m_connectionAmount - 1; i++)
            {
                //if the connection isnt null then draw a line of it this whole function is self explaining
                if (node.m_connectedNodes[i] != null)
                    Debug.DrawLine(node.m_position,node.m_connectedNodes[i].m_position);
            }
        }
        Debug.Log("DRAW PASSED");
    }
    
}
