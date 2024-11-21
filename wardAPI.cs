using System;
using UnityEngine;
using WardIsLove.API;

public class WardInteractionMod : MonoBehaviour
{
    private void OnEnable()
    {
        // Subscribe to WardIsLove events
        API.OnWardEntered += HandleWardEntered;
        API.OnWardExited += HandleWardExited;
        API.OnBubbleOn += HandleBubbleOn;
        API.OnBubbleOff += HandleBubbleOff;
        API.OnDamageTaken += HandleDamageTaken;
    }

    private void OnDisable()
    {
        // Unsubscribe from events to avoid memory leaks
        API.OnWardEntered -= HandleWardEntered;
        API.OnWardExited -= HandleWardExited;
        API.OnBubbleOn -= HandleBubbleOn;
        API.OnBubbleOff -= HandleBubbleOff;
        API.OnDamageTaken -= HandleDamageTaken;
    }

    private void HandleWardEntered(Vector3 position)
    {
        Debug.Log($"Entered a ward at position: {position}");
    }

    private void HandleWardExited(Vector3 position)
    {
        Debug.Log($"Exited a ward at position: {position}");
    }

    private void HandleBubbleOn(Vector3 position)
    {
        Debug.Log($"Ward bubble shield activated at position: {position}");
    }

    private void HandleBubbleOff(Vector3 position)
    {
        Debug.Log($"Ward bubble shield deactivated at position: {position}");
    }

    private void HandleDamageTaken(Vector3 position, float damage)
    {
        Debug.Log($"Ward at {position} took {damage} damage");
    }
    /*

    private void Update()
    {
        // Check if the API is loaded
        if (!API.IsLoaded())
        {
            Debug.Log("WardIsLove API is not loaded.");
            return;
        }

        // Example of checking if a point is inside a ward
        Vector3 pointToCheck = new Vector3(0, 0, 0); // Replace with your coordinates
        bool isInsideWard = API.IsInsideWard(pointToCheck);
        if (isInsideWard)
        {
            Debug.Log($"Point {pointToCheck} is inside a ward.");
        }

        // Example of disabling a ward at a specific point
        if (Input.GetKeyDown(KeyCode.D))
        {
            API.DisableWardPlayerIsIn(pointToCheck, destroyPiece: false);
            Debug.Log($"Disabled ward at {pointToCheck}");
        }

        // Example of destroying a ward piece at a specific point
        if (Input.GetKeyDown(KeyCode.X))
        {
            GameObject wardObject = API.GetWardObject(pointToCheck);
            if (wardObject != null)
            {
                API.DestroyWard(wardObject.GetComponent<Piece>());
                Debug.Log($"Destroyed ward piece at {pointToCheck}");
            }
            else
            {
                Debug.Log($"No ward found at {pointToCheck} to destroy.");
            }
        }
    }
    */
}
