using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.UrdfImporter;
using RosMessageTypes.Sensor;

public class UR5eJointStateSubscriber : MonoBehaviour
{
    ROSConnection ros;

    [Tooltip("Drag the UR5e ArticulationBody joints here in the Inspector")]
    public ArticulationBody[] Joints;

    [Tooltip("The exact ROS 2 topic name MoveIt broadcasts to")]
    public string jointStatesTopic = "/joint_states";
    private Dictionary<string, int> jointNameToIndex = new Dictionary<string, int>();

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<JointStateMsg>(jointStatesTopic, JointStatesCallback);
        for (int i = 0; i < Joints.Length; i++)
        {
            string jointName = Joints[i].GetComponent<UrdfJoint>().jointName;

            if (!jointNameToIndex.ContainsKey(jointName))
            {
                jointNameToIndex.Add(jointName, i);
            }
        }
    }

    void JointStatesCallback(JointStateMsg msg)
    {
        for (int i = 0; i < msg.name.Length; i++)
        {
            string incomingJointName = msg.name[i];
            if (jointNameToIndex.TryGetValue(incomingJointName, out int unityJointIndex))
            {
                float targetAngleDegrees = (float)msg.position[i] * Mathf.Rad2Deg;
                Joints[unityJointIndex].SetDriveTarget(ArticulationDriveAxis.X, targetAngleDegrees);
            }
        }
    }
}