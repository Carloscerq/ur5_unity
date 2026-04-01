using System.Collections;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using RosMessageTypes.Geometry;
using RosMessageTypes.Std;

public class PickAndPlaceController : MonoBehaviour
{
    public enum SystemState { Idle, ObjectSelected, RobotMoving }
    public SystemState currentState = SystemState.Idle;

    [Header("ROS 2 Settings")]
    public string targetPoseTopic = "/target_pose";
    public string moveItFrameId = "base_link"; // The reference frame MoveIt is using
    private ROSConnection ros;

    [Header("Scene References")]
    public GameObject selectedObject;
    public Transform endEffector; // Drag your UR5's tool0 or Robotiq base link here
    
    [Header("Pick & Place Offsets")]
    [Tooltip("How high above the object the arm should be during transit")]
    public float zOffset = 0.15f; 

    [Header("Visual Feedback")]
    public Material selectedMaterial;
    private Material originalMaterial;
    private MeshRenderer selectedRenderer;

    void Start()
    {
        // Initialize ROS and register the publisher
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<PoseStampedMsg>(targetPoseTopic);
    }

    void Update()
    {
        if (currentState == SystemState.RobotMoving) return;

        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                HandleClick(hit);
            }
        }
    }

    private void HandleClick(RaycastHit hit)
    {
        if (currentState == SystemState.Idle && hit.collider.CompareTag("Pickable"))
        {
            SelectObject(hit.collider.gameObject);
        }
        else if (currentState == SystemState.ObjectSelected && hit.collider.CompareTag("PlaceableSurface"))
        {
            Vector3 targetDestination = hit.point;
            // Add half the object's height so it sits ON the table, not IN it
            targetDestination.y += selectedObject.GetComponent<Collider>().bounds.extents.y; 
            StartCoroutine(ExecutePickAndPlace(targetDestination));
        }
        else if (currentState == SystemState.ObjectSelected)
        {
            DeselectObject();
        }
    }

    private void SelectObject(GameObject obj)
    {
        selectedObject = obj;
        currentState = SystemState.ObjectSelected;

        selectedRenderer = selectedObject.GetComponent<MeshRenderer>();
        if (selectedRenderer != null && selectedMaterial != null)
        {
            originalMaterial = selectedRenderer.material;
            selectedRenderer.material = selectedMaterial;
        }
        Debug.Log($"Selected: {selectedObject.name}. Now click a destination.");
    }

    private void DeselectObject()
    {
        if (selectedRenderer != null && originalMaterial != null)
        {
            selectedRenderer.material = originalMaterial;
        }
        selectedObject = null;
        currentState = SystemState.Idle;
    }

    // --- ROS Integration --- //
    
    private void PublishTargetPose(Vector3 position, Quaternion rotation)
    {
        // Convert Unity coordinates to ROS FLU (Forward-Left-Up) coordinates
        PoseMsg pose = new PoseMsg
        {
            position = position.To<FLU>(),
            orientation = rotation.To<FLU>()
        };

        PoseStampedMsg poseMessage = new PoseStampedMsg
        {
            header = new HeaderMsg { frame_id = moveItFrameId },
            pose = pose
        };

        ros.Publish(targetPoseTopic, poseMessage);
        Debug.Log($"Published Pose to {targetPoseTopic}");
    }

    private IEnumerator ExecutePickAndPlace(Vector3 destination)
    {
        currentState = SystemState.RobotMoving;

        if (selectedRenderer != null && originalMaterial != null)
            selectedRenderer.material = originalMaterial;

        // Ensure the end-effector points down (Adjust this Quaternion based on your specific UR5 setup)
        Quaternion downwardOrientation = Quaternion.Euler(180, 0, 0);

        // 1. Move to Object
        Vector3 graspPosition = selectedObject.transform.position;
        Debug.Log("Sending MoveIt to grasp position...");
        PublishTargetPose(graspPosition, downwardOrientation);
        
        // Wait for MoveIt to plan and the arm to reach the object (with a 10-second timeout)
        yield return StartCoroutine(WaitForRobotToReach(graspPosition, 10f));

        // 2. Grasp (Fake it by parenting and disabling physics)
        selectedObject.GetComponent<Rigidbody>().isKinematic = true;
        selectedObject.transform.SetParent(endEffector);
        Debug.Log("Object Grasped!");

        // 3. Move to Destination
        Debug.Log("Sending MoveIt to destination...");
        PublishTargetPose(destination, downwardOrientation);
        
        yield return StartCoroutine(WaitForRobotToReach(destination, 10f));

        // 4. Release
        selectedObject.transform.SetParent(null);
        selectedObject.GetComponent<Rigidbody>().isKinematic = false;
        Debug.Log("Object Released!");

        // Reset
        selectedObject = null;
        currentState = SystemState.Idle;
    }

    // Helper coroutine to wait until the end-effector physically reaches the target in Unity
    private IEnumerator WaitForRobotToReach(Vector3 targetPos, float timeout)
    {
        float timer = 0f;
        // Wait until distance is less than 5cm, or we time out
        while (Vector3.Distance(endEffector.position, targetPos) > 0.05f)
        {
            timer += Time.deltaTime;
            if (timer > timeout)
            {
                Debug.LogWarning("Robot move timed out! MoveIt plan may have failed.");
                break;
            }
            yield return null;
        }
        
        // Brief pause to let physics settle
        yield return new WaitForSeconds(0.5f);
    }
}