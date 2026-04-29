using UnityEngine;

public class RobotMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] float moveSpeed = 10f;         // Скорость движения вперёд (м/с)
    [SerializeField] float rotationSpeed = 10f;    // Скорость поворота (градусы/с) — 90°/с = полный круг за 4 секунды

    [Header("Debug")]
    [SerializeField] bool isMoving = true;         // Выключатель движения (для тестов в инспекторе)

    void FixedUpdate()
    {
        transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime);
        transform.Rotate(0f, rotationSpeed * Time.deltaTime, 0f);
    }
}