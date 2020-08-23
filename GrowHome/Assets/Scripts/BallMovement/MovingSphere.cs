﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MovingSphere: MonoBehaviour
{

    [SerializeField, Range (0f, 100f)]
    float maxSpeed = 10f;

    [SerializeField, Range(0f, 100f)]
    float maxAcceleration = 10, maxAirAcceleration = 1f;

    [SerializeField]
    Rect allowedArea = new Rect(-5f, -5f, 10f, 10f);

    //[SerializeField, Range(0f, 1f)]
    //float bounciness = 0.5f;

    [SerializeField, Range(0f, 10f)]
    float jumpHeight = 2f;

    [SerializeField, Range(0, 5)]
    int maxExtraAirJumps = 0;

    [SerializeField, Range(0f, 90f)]
    float maxGroundAngle = 25f, maxStairsAngle = 50f;

    [SerializeField, Range(0f, 100f)]
    float maxSnapSpeed = 100f;

    [SerializeField, Min(0f)]
    float probeDistance = 1f;

    [SerializeField]
    LayerMask probeMask = -1, stairsMask = -1;

    [SerializeField]
    Transform playerInputSpace = default;

    Vector3 velocity, desiredVelocity, connectionVelocity;

    Rigidbody body, connectedBody, previousConnectedBody;

    bool desiredJump;
    int groundContactCount, steepContactCount;

    bool OnGround => groundContactCount > 0;

    bool OnSteep => steepContactCount > 0;

    int jumpPhase;

    float minGroundDotProduct, minStairsDotProduct, minClimbDotProduct;

    Vector3 contactNormal, steepNormal, climbNormal;

    int stepsSinceLastGrounded, stepsSinceLastJump;

    Vector3 upAxis, rightAxis, forwardAxis;

    [SerializeField, Range(90, 180)]
    float maxClimbAngle = 140f;

    Vector3 connectionWorldPosition, connectionLocalPosition;

    void OnValidate()
    {
        minGroundDotProduct = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
        minStairsDotProduct = Mathf.Cos(maxStairsAngle * Mathf.Deg2Rad);
        minClimbDotProduct = Mathf.Cos(maxClimbAngle * Mathf.Deg2Rad);
    }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        OnValidate();
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 playerInput;
        playerInput.x = Input.GetAxis("Horizontal");
        playerInput.y = Input.GetAxis("Vertical");
        playerInput = Vector2.ClampMagnitude(playerInput, 1f);

        if (playerInputSpace)
        {
            rightAxis = ProjectOnDirectionPlane(playerInputSpace.right, upAxis);
            forwardAxis =
                ProjectOnDirectionPlane(playerInputSpace.forward, upAxis);
        } else
        {
            rightAxis = ProjectOnDirectionPlane(Vector3.right, upAxis);
            forwardAxis =
                ProjectOnDirectionPlane(Vector3.forward, upAxis);
        }
        desiredVelocity =
            new Vector3(playerInput.x, 0f, playerInput.y) * maxSpeed;


        desiredJump |= Input.GetButtonDown("Jump");

        //GetComponent<Renderer>().material.SetColor("_Color", OnGround ? Color.red: Color.white);

    }

    void FixedUpdate()
    {
        Vector3 gravity = CustomGravity.GetGravity(body.position, out upAxis);
        UpdateState();
        AdjustVelocity();

        if (desiredJump)
        {
            desiredJump = false;
            Jump(gravity);
        }

        velocity += gravity * Time.deltaTime;

        body.velocity = velocity;
        ClearState();
    }

    void ClearState()
    {
        groundContactCount = steepContactCount = 0 ;
        contactNormal = steepNormal = climbNormal = connectionVelocity =  Vector3.zero;
        previousConnectedBody = connectedBody;
        connectedBody = null;
    }

    void Jump(Vector3 gravity)
    {
        Vector3 jumpDirection;
        if (OnGround)
        {
            jumpDirection = contactNormal;
        } else if (OnSteep)
        {
            jumpDirection = steepNormal;
            //jumpPhase = 0; //Re-Enable if you want extra jumps to reset on wall jumps
        } else if (maxExtraAirJumps > 0 && jumpPhase <= maxExtraAirJumps)
        {
            if (jumpPhase == 0)
            {
                jumpPhase = 1;
            }
            jumpDirection = contactNormal;
            jumpPhase++;
        } else
        {
            return;
        }

        stepsSinceLastJump = 0;
        
        float jumpSpeed = Mathf.Sqrt(2f * gravity.magnitude * jumpHeight);
        jumpDirection = (jumpDirection + upAxis).normalized;
        float alignedSpeed = Vector3.Dot(velocity, jumpDirection);
        if (alignedSpeed > 0f)
        {
            jumpSpeed = Mathf.Max(jumpSpeed - alignedSpeed, 0f);
        }

        /*
        if (OnSteep && !OnGround) //consistent wall jumps, bad 
        {
            jumpSpeed = Mathf.Sqrt(2f * gravity.magnitude * wallJumpHeight);
            velocity = jumpDirection * jumpSpeed; 
        } else
        {

        */
            velocity += jumpDirection * jumpSpeed; // accumulate speed with jump
        //}
       
    }

    void UpdateState()
    {
        stepsSinceLastGrounded += 1;
        stepsSinceLastJump += 1;
        velocity = body.velocity;
        if (OnGround || SnapToGround() || CheckSteepContacts())
        {
            stepsSinceLastGrounded = 0;
            if (stepsSinceLastJump > 1)
            {
                jumpPhase = 0;
            }
            if (groundContactCount > 1)
            {
                contactNormal.Normalize();
            }
        } else
        {
            contactNormal = upAxis;
        }

        if (connectedBody)
        {
            if (connectedBody.isKinematic || connectedBody.mass >= body.mass)
            {
                UpdateConnectionState();
            }
        }
    }

    void UpdateConnectionState()
    {
        if (connectedBody == previousConnectedBody)
        {
            Vector3 connectionMovement =
                connectedBody.transform.TransformPoint(connectionLocalPosition) - 
                connectionWorldPosition;
            connectionVelocity = connectionMovement / Time.deltaTime;
        }
        connectionWorldPosition = body.position;
        connectionLocalPosition = connectedBody.transform.InverseTransformPoint(
            connectionWorldPosition);
    }

    bool CheckSteepContacts()
    {
        if (steepContactCount > 1)
        {
            steepNormal.Normalize();
            float upDot = Vector3.Dot(upAxis, steepNormal);
            if (upDot >= minGroundDotProduct)
            {
                groundContactCount = 1;
                contactNormal = steepNormal;
                return true;
            }
        }
        return false;
    }

    void OnCollisionEnter(Collision collision)
    {
        EvaluateCollision(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        EvaluateCollision(collision);
    }

    void EvaluateCollision(Collision collision) {
        float minDot = GetMinDot(collision.gameObject.layer);
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            float upDot = Vector3.Dot(upAxis, normal);
            if (upDot >= minDot)
            {
                groundContactCount += 1;
                contactNormal += normal;
                connectedBody = collision.rigidbody;
            }
            else if (upDot > -0.01f)
            {
                steepContactCount++;
                steepNormal += normal;
                if (groundContactCount == 0)
                {
                    connectedBody = collision.rigidbody;
                }
            }
        }
    }

    //Vector3 ProjectOnContactPlane(Vector3 vector)
    //{
    //    return vector - contactNormal * Vector3.Dot(vector, contactNormal);
    //}

    Vector3 ProjectOnDirectionPlane (Vector3 direction, Vector3 normal)
    {
        return (direction - normal * Vector3.Dot(direction, normal)).normalized;
    }

    void AdjustVelocity()
    {
        Vector3 xAxis = ProjectOnDirectionPlane(rightAxis, contactNormal);
        Vector3 zAxis = ProjectOnDirectionPlane(forwardAxis, contactNormal);

        Vector3 relativeVelocity = velocity - connectionVelocity;
        float currentX = Vector3.Dot(relativeVelocity, xAxis);
        float currentZ = Vector3.Dot(relativeVelocity, zAxis);

        float acceleration = OnGround ? maxAcceleration : maxAirAcceleration;
        float maxSpeedChange = acceleration * Time.deltaTime;

        float newX =
            Mathf.MoveTowards(currentX, desiredVelocity.x, maxSpeedChange);
        float newZ =
            Mathf.MoveTowards(currentZ, desiredVelocity.z, maxSpeedChange);

        velocity += xAxis * (newX - currentX) + zAxis * (newZ - currentZ);
    }

    bool SnapToGround()
    {
        if (stepsSinceLastGrounded > 1 || stepsSinceLastJump <= 2)
        {
            return false;
        }
        float speed = velocity.magnitude;
        if (speed > maxSnapSpeed)
        {
            return false;
        }

        if (!Physics.Raycast(body.position, -upAxis, out RaycastHit hit, probeDistance, probeMask))
        {
            return false;
        }

        float upDot = Vector3.Dot(upAxis, hit.normal);
        if (upDot < GetMinDot(hit.collider.gameObject.layer))
        {
            return false;
        }

        groundContactCount = 1;
        contactNormal = hit.normal;
        
        float dot = Vector3.Dot(velocity, hit.normal);
        velocity = (velocity - hit.normal * dot).normalized * speed;
        connectedBody = hit.rigidbody;
        return true;
    }

    float GetMinDot (int layer)
    {
        return (stairsMask & (1 << layer)) == 0 ? minGroundDotProduct : minStairsDotProduct;
    }
}


/* // Confine to an Area
        if (newPosition.x < allowedArea.xMin)
        {
            newPosition.x = allowedArea.xMin;
            velocity.x = -velocity.x * bounciness;
        }
        else if (newPosition.x > allowedArea.xMax)
        {
            newPosition.x = allowedArea.xMax;
            velocity.x = -velocity.x * bounciness;
        }
        if (newPosition.z < allowedArea.yMin)
        {
            newPosition.z = allowedArea.yMin;
            velocity.z = -velocity.z * bounciness;
        }
        else if (newPosition.z > allowedArea.yMax)
        {
            newPosition.z = allowedArea.yMax;
            velocity.z = -velocity.z * bounciness;
        }

 */
