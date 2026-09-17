// Автор прочитал и понимает что тут происходит.

using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class RobotMovement : MonoBehaviour
{
    [SerializeField] float moveSpeed = 7f;
    [SerializeField] float rotationSpeed = 50f;
    [SerializeField] float targetDistance = 30f;
    [SerializeField] float arrivalDistance = 1.5f;
    [SerializeField] float turnDoneAngle = 0.1f;
    [SerializeField] int targetAttempts = 20;
    [SerializeField] LayerMask terrainLayer;
    [SerializeField] string terrainName = "Terrain";

    Rigidbody rb;
    bool hasTarget;
    bool turning;
    Vector3 moveDirection;
    Quaternion targetRotation;
    float distanceLeft;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        /*

        */
        if (!hasTarget)
        {
            Vector3 currentPosition = rb.position;

            // Eсли у робота нет цели, то он ищет случайную точку на земле в пределах targetDistance и пытается туда пойти.
            for (int i = 0; i < targetAttempts; i++) 
            {
                Vector2 random = Random.insideUnitCircle.normalized;
                if (random == Vector2.zero)
                    random = Vector2.forward;
                Vector3 point = currentPosition + new Vector3(random.x, 0f, random.y) * targetDistance;
                // берем рандомную точку в пределах targetDistance от текущей позиции робота
                    // 1. делаем random: рандомную точку (Random.insideUnitCircle) 
                    // 2. на границе круга (за это отвечает .normalized)
                    // 3. если вдруг случайно получилось (0,0), то продолжаем ехать вперед (Vector2.forward)
                    // 4. выбрав направление, ставим целевую точку в этом направлении на расстоянии targetDistance от текущей позиции робота
                Ray ray = new Ray(new Vector3(point.x, 1000f, point.z), Vector3.down);
                // стреляем в эту точку лучом с высоты 1000

                if (!Physics.Raycast(ray, out RaycastHit hit, 2000f, terrainLayer, QueryTriggerInteraction.Ignore))
                    continue; // если луч не попал в землю, то переходим к следующей попытке поиска точки

                Collider collider = hit.collider; // Забираем коллайдер обьекта в который мы попали лучом
                if (collider.GetComponent<Terrain>() == null && collider.name != terrainName && collider.transform.root.name != terrainName)
                    continue; // Если это не Terrain, то продолжаем искать новую точку.

                moveDirection = hit.point - currentPosition; // позиция куда мы идем = позиция точки на земле - текущая позиция робота
                moveDirection.y = 0f; // обнуляем высоту
                distanceLeft = moveDirection.magnitude; // расстояние до точки = длина вектора в направлении движения
                if (distanceLeft <= arrivalDistance) // если расстояние до точки меньше arrivalDistance, то продолжаем искать новую точку
                                                     // ГАВНО ГАВНО ГАВНО
                                                     // НАДО ПЕРЕПИСАТЬ ГАВНО
                    continue;

                moveDirection.Normalize();
                targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
                turning = Quaternion.Angle(rb.rotation, targetRotation) > turnDoneAngle;
                hasTarget = true;
                break;
            }

            return;
        }

        // if (!hasTarget)
        //     return;

        if (turning)
        {
            Quaternion nextRotation = Quaternion.RotateTowards(rb.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
            rb.MoveRotation(nextRotation);
            turning = Quaternion.Angle(nextRotation, targetRotation) > turnDoneAngle;
            return;
        }

        if (distanceLeft <= arrivalDistance)
        {
            hasTarget = false;
            return;
        }

        float step = Mathf.Min(moveSpeed * Time.fixedDeltaTime, distanceLeft);
        rb.MovePosition(rb.position + moveDirection * step);
        distanceLeft -= step;
    }

    // ---------------------------------------------------------------------
    // Service methods
    // ---------------------------------------------------------------------

}
