using System.Text;
using TMPro;
using UnityEngine;

public class PlayerDebugHud : MonoBehaviour
{
    [SerializeField] TMP_Text interfaceText;

    readonly StringBuilder builder = new StringBuilder(256);

    // Rebuilds the player debug text from the latest movement data.
    public void UpdateDebug(
        bool isGrounded,
        float verticalVelocity,
        string upperObjectName,
        string lowerObjectName,
        string stateName,
        Transform currentGround,
        Vector3 currentGroundVelocity,
        Vector3 groundNormal,
        Transform parent,
        string animationDebug)
    {

        builder.Clear();
        builder.Append("Grounded: ").Append(isGrounded).Append('\n');
        builder.Append("VelY: ").Append(verticalVelocity.ToString("F2")).Append('\n');
        builder.Append("Upper: ").Append(upperObjectName).Append('\n');
        builder.Append("Lower: ").Append(lowerObjectName).Append('\n');
        builder.Append("State: ").Append(stateName).Append('\n');
        builder.Append("Ground: ").Append(currentGround != null ? currentGround.name : "none").Append('\n');
        builder.Append("GroundVel: ").Append(currentGroundVelocity.magnitude.ToString("F2")).Append('\n');
        builder.Append("GroundAngle: ").Append(Vector3.Angle(groundNormal, Vector3.up).ToString("F1")).Append('\n');
        builder.Append("Parent: ").Append(parent != null ? parent.name : "none").Append('\n');
        builder.Append(animationDebug);

        interfaceText.text = builder.ToString();
    }
}
