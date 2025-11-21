using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Player_controller : MonoBehaviour
{
    [Tooltip("Die Kamera bzw. der Kopf des Spielers")]
    public GameObject head_cam;

    [Tooltip("Fortbewegung erlaubt (true) oder verboten (false)")]
    public bool move_allowed = true;

    [Tooltip("Normale Laufgeschwindigkeit")]
    public float walk_speed = 5.0f;

    [Tooltip("Beim Rennen wird walk_speed um diesen Faktor erhöht")]
    public float run_speed_factor = 1.5f;

    [Tooltip("Bodenkontaktstrahl nach unten")]
    public float down_length = 1.1f;

    [Tooltip("Kopfbewegung erlaubt (true) oder verboten (false)")]
    public bool turn_allowed = true;

    [Tooltip("Drehgeschwindigkeit der Kamera / des Kopfes")]
    public float cam_turn_speed = 2.0f;

    [Tooltip("Maximaler Winkel / Rotationslimit der Kamera nach oben und unten")]
    public float cam_x_rot_limit = 55.0f;

    [Tooltip("Interaktion möglich (true) oder nicht möglich (false)")]
    public bool interaction_allowed = true;

    [Header("Interaktion (robust)")]
    [Tooltip("Reichweite der Hand des Spielers")]
    public float hand_range = 2.5f;

    [Tooltip("Nur diese Layer werden für Interaktionen berücksichtigt")]
    public LayerMask interactableMask = ~0;

    [Tooltip("Triggers berücksichtigen? (falls deine Interaktoren Trigger-Collider sind)")]
    public bool triggersAllowed = false;

    [Tooltip("Radius des SphereCast (statt dünnem Ray)")]
    [Range(0.02f, 0.25f)] public float handSphereRadius = 0.08f;

    [Tooltip("Wie oft pro Sekunde wird geprüft (0 = jede LateUpdate-Frame)")]
    [Range(0f, 0.2f)] public float handCheckInterval = 0.02f;

    [Tooltip("Hysterese: Ziel bleibt noch so lange 'kleben'")]
    [Range(0f, 0.5f)] public float handStickyTime = 0.2f;

    [Tooltip("Kleiner Vorwärts-Offset für den Cast (verhindert 'Start inside')")]
    [Range(0f, 0.05f)] public float castStartOffset = 0.015f;

    [Tooltip("Benutzen-Taste des Spielers")]
    public string use_key = "e";

    [Tooltip("Textobjekt der UI (TextMeshPro)")]
    public TextMeshProUGUI hand_ui_texter;
    public Image hand_ui_background;

    [Tooltip("Fadenkreuz im UI")]
    public Image crosshair_ui;

    [Tooltip("Standardbild des Fadenkreuzes")]
    public Sprite crosshair_standard;

    [Tooltip("Positiv-Variante des Fadenkreuzes")]
    public Sprite crosshair_positive;

    [Tooltip("Negativ-Variante des Fadenkreuzes")]
    public Sprite crosshair_negative;

    [Tooltip("Strecke der Auf- und Abbewegung des Kopfes beim Laufen")]
    public float head_hop_height = 0.25f;

    [Tooltip("Zeit für die Auf- und Abbewegung des Kopfes beim Laufen")]
    public float head_hop_time = 0.075f;

    [Tooltip("Kriechen und Ducken zulässig")]
    public bool crouch_allowed = true;

    [Tooltip("Beim Kriechen wird walk_speed um diesen Faktor verringert")]
    public float crouch_speed_factor = 0.75f;

    [Tooltip("Geschwindigkeit, wie schnell die geduckte Haltung erreicht wird")]
    public float crouch_pos_speed = 5.0f;

    [Tooltip("Wieviel wird die Höhe des Spielers beim Ducken reduziert")]
    public float crouch_pos_factor = 0.25f;

    [Tooltip("Schritt und Sprungssounds aktivieren")]
    public bool sounds_active;

    [Tooltip("Zufällige Schrittgeräuschauswahl")]
    public List<AudioClip> step_sounds;

    [Tooltip("Sound beim Absprung vom Boden")]
    public AudioClip jump_step_sound;

    [Tooltip("Sound beim Landen auf dem Boden")]
    public AudioClip jump_land_sound;

    [Header("Debug")]
    public bool debugInteraction = false;

    // --- intern
    float cam_x_rot_val = 0f; // pitch
    float yaw = 0f;           // yaw

    GameObject cam;
    AudioSource audio_source;
    Rigidbody my_rigid_body;

    RaycastHit down_ray;
    bool floor_touch;
    bool floor_touch_before;

    // Interaktion
    RaycastHit hand_ray;
    bool hand_touch;
    GameObject hand_ob;

    // NonAlloc-Puffer
    RaycastHit[] handHits = new RaycastHit[8];
    Collider[] overlapCols = new Collider[8];
    float nextHandCheckTime;
    GameObject lastHandOb;
    float lastHitTime;

    // UI-State-Cache
    string cachedText = null;
    Sprite cachedCrosshair = null;
    float cachedBGAlpha = -1f;

    Vector3 cam_standard_pos;
    Vector3 original_size_v3;

    bool key_left, key_forward, key_backwards, key_right, key_run, key_crouch, key_jump, key_use, use_trigger;
    bool jump_trigger, jump_has_ended;
    float head_2_target_lerp_val, head_2_target_lerp_min = 0.15f, head_2_target_lerp_max = 0.65f;
    bool lerp_reset;

    string use_key_upper = "E";

    void Awake()
    {
        my_rigid_body = GetComponentInChildren<Rigidbody>();
        audio_source = GetComponent<AudioSource>();
        original_size_v3 = this.transform.localScale;

        if (!my_rigid_body)
        {
            Debug.LogError($"[Player_controller] Kein Rigidbody gefunden! Entferne Spieler.");
            Destroy(gameObject);
            return;
        }

        my_rigid_body.interpolation = RigidbodyInterpolation.Interpolate;
        my_rigid_body.collisionDetectionMode = CollisionDetectionMode.Discrete;

        if (!head_cam)
        {
            var found = transform.Find("head_cam");
            if (!found)
            {
                Debug.LogError($"[Player_controller] Kein 'head_cam' gefunden!");
                Destroy(gameObject);
                return;
            }
            cam = found.gameObject;
            head_cam = cam;
        }
        else cam = head_cam;

        if (sounds_active && !audio_source)
        {
            Debug.LogWarning("[Player_controller] Kein AudioSource gefunden – Sounds deaktiviert.");
            sounds_active = false;
        }
    }

    void Start()
    {
        jump_has_ended = true;
        head_2_target_lerp_val = 0f;
        use_key_upper = string.IsNullOrEmpty(use_key) ? "" : use_key.ToUpper();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (interaction_allowed && crosshair_ui) crosshair_ui.sprite = crosshair_standard;

        my_rigid_body.drag = 1.0f;
        my_rigid_body.angularDrag = 1.0f;

        cam_standard_pos = cam.transform.localPosition;

        cam.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        yaw = transform.eulerAngles.y;
        cam_x_rot_val = 0f;

        cachedCrosshair = crosshair_standard;
        if (crosshair_ui) crosshair_ui.sprite = cachedCrosshair;
        SetBGAlpha(0f, force: true);
        SetUIText("", force: true);

#if UNITY_WEBGL && !UNITY_EDITOR
        // <<< Das ist entscheidend: im selben Button-Klick den Browser-Pointer-Lock holen
        WebGLPointerLock.Acquire();
#endif
    }

    void Update()
    {
        if (turn_allowed)
        {
            float mouseX = Input.GetAxis("Mouse X") * cam_turn_speed;
            float mouseY = Input.GetAxis("Mouse Y") * cam_turn_speed;

            yaw += mouseX;
            cam_x_rot_val += -mouseY;
            cam_x_rot_val = Mathf.Clamp(cam_x_rot_val, -cam_x_rot_limit, cam_x_rot_limit);
        }

        key_left = Input.GetKey("a") || Input.GetKey(KeyCode.LeftArrow);
        key_forward = Input.GetKey("w") || Input.GetKey(KeyCode.UpArrow);
        key_backwards = Input.GetKey("s") || Input.GetKey(KeyCode.DownArrow);
        key_right = Input.GetKey("d") || Input.GetKey(KeyCode.RightArrow);

        key_crouch = Input.GetKey(KeyCode.LeftControl);
        key_jump = Input.GetKeyDown(KeyCode.Space);
        key_run = Input.GetKey(KeyCode.LeftShift);

        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.LeftShift)) lerp_reset = true;

        key_use = Input.GetKeyDown(use_key);
        if (key_jump) jump_trigger = true;
        if (key_use) use_trigger = true;
    }

    void FixedUpdate()
    {
        floor_touch_before = floor_touch;

        if (Physics.Raycast(transform.position, -transform.up, out down_ray, down_length))
            floor_touch = true;
        else
            floor_touch = false;

        if (!floor_touch_before && floor_touch)
        {
            jump_has_ended = true;
            play_sound(jump_land_sound, 1.0f, true);
        }

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 right = yawRot * Vector3.right;
        Vector3 forward = yawRot * Vector3.forward;

        bool walking = false;
        bool running = false;
        bool crouching = false;

        Vector3 mover_sideways = Vector3.zero;
        Vector3 mover_forward = Vector3.zero;

        if (key_right) { mover_sideways = right; walking = true; }
        else if (key_left) { mover_sideways = -right; walking = true; }

        if (key_forward) { mover_forward = forward; walking = true; }
        else if (key_backwards) { mover_forward = -forward; walking = true; }

        Vector3 mover = mover_forward + mover_sideways;

        if ((key_forward && key_right) || (key_forward && key_left) || (key_backwards && key_right) || (key_backwards && key_left))
            mover *= 0.6f;

        Vector3 crouch_scale = Vector3.one;
        if (key_crouch && crouch_allowed)
        {
            crouching = true;
            crouch_scale = Vector3.Lerp(transform.localScale, new Vector3(original_size_v3.x, original_size_v3.y - crouch_pos_factor, original_size_v3.z), Time.deltaTime * crouch_pos_speed);
        }
        else
        {
            crouch_scale = Vector3.Lerp(transform.localScale, original_size_v3, Time.deltaTime * crouch_pos_speed);
        }
        if (transform.localScale != crouch_scale)
            transform.localScale = crouch_scale;

        Vector3 velocity = Vector3.zero;
        if (move_allowed && (walking || crouching))
        {
            float speed = walk_speed;
            if (key_run && !crouching) { speed *= run_speed_factor; running = true; }
            if (crouching) { speed *= crouch_speed_factor; }

            velocity = mover.normalized * speed;
        }

        my_rigid_body.velocity = new Vector3(velocity.x, my_rigid_body.velocity.y, velocity.z);

        if (move_allowed && floor_touch && (walking || running))
        {
            if (lerp_reset)
            {
                head_2_target_lerp_val = head_2_target_lerp_min;
                lerp_reset = false;
            }
        }
        else
        {
            head_2_target_lerp_val = head_2_target_lerp_min;
        }
    }

    void LateUpdate()
    {
        // --- Kamera & Headbob
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cam.transform.localRotation = Quaternion.Euler(cam_x_rot_val, 0f, 0f);

        float input_val = Time.timeSinceLevelLoad / head_hop_time;
        float distance = head_hop_height * Mathf.Sin(input_val);
        Vector3 cam_target_pos = cam_standard_pos + Vector3.up * distance;

        if (head_2_target_lerp_val < head_2_target_lerp_max)
            head_2_target_lerp_val += Time.deltaTime;

        cam.transform.localPosition = Vector3.Lerp(cam.transform.localPosition, cam_target_pos, head_2_target_lerp_val);

        // --- Interaktion (präzise)
        if (!interaction_allowed) return;

        // Optionales Rate-Limit konsumiert nur Use-Buffer
        if (handCheckInterval > 0f && Time.unscaledTime < nextHandCheckTime)
        {
            TryConsumeUse();
            return;
        }
        nextHandCheckTime = Time.unscaledTime + (handCheckInterval <= 0f ? 0f : handCheckInterval);

        hand_touch = false;
        hand_ob = null;

        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;
        var qti = triggersAllowed ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

        // 1) EXACT: dünner Ray entlang Fadenkreuz
        if (Physics.Raycast(origin, dir, out hand_ray, hand_range, interactableMask, qti))
        {
            // sich selbst ignorieren
            var go = hand_ray.collider.attachedRigidbody ? hand_ray.collider.attachedRigidbody.gameObject
                                                         : hand_ray.collider.gameObject;
            if (go.transform.root != transform.root)
            {
                hand_touch = true;
                hand_ob = go;
                lastHandOb = hand_ob;          // (falls du später wieder Sticky möchtest)
                lastHitTime = Time.unscaledTime;
            }
        }

        // 2) Sonderfall: Kamera steckt bereits IM Collider -> nur akzeptieren, wenn Collider.Raycast trifft
        if (!hand_touch)
        {
            // sehr kleine Overlap-Sphäre (nur "wirklich inside")
            int overlapCount = Physics.OverlapSphereNonAlloc(
                origin,
                0.005f, // 5mm
                overlapCols,
                interactableMask,
                qti
            );

            if (overlapCount > 0)
            {
                float bestDist = float.MaxValue;
                RaycastHit bestHit = default;
                GameObject bestGO = null;

                for (int i = 0; i < overlapCount; i++)
                {
                    var col = overlapCols[i];
                    if (!col) continue;

                    // Self ignorieren
                    var root = col.attachedRigidbody ? col.attachedRigidbody.transform.root : col.transform.root;
                    if (root == transform.root) continue;

                    // Prüfen, ob wirklich inside (ClosestPoint == origin)
                    if (Vector3.SqrMagnitude(col.ClosestPoint(origin) - origin) > 1e-8f) continue;

                    // Collider-spezifischer Raycast von der Kamera entlang Forward
                    RaycastHit hit;
                    if (col.Raycast(new Ray(origin, dir), out hit, hand_range))
                    {
                        if (hit.distance < bestDist)
                        {
                            bestDist = hit.distance;
                            bestHit = hit;
                            bestGO = col.attachedRigidbody ? col.attachedRigidbody.gameObject : col.gameObject;
                        }
                    }
                }

                if (bestGO != null)
                {
                    hand_touch = true;
                    hand_ray = bestHit;
                    hand_ob = bestGO;
                    lastHandOb = hand_ob;
                    lastHitTime = Time.unscaledTime;

                    if (debugInteraction)
                        Debug.Log($"[Inside+Raycast] {hand_ob.name} (Tag:{hand_ob.tag}) Dist:{hand_ray.distance:F2}");
                }
            }
        }

        // 3) KEINE Sticky/Hysterese – für maximale Exaktheit.
        // Falls du dein altes "Kleben" behalten willst: setze handStickyTime sehr klein (z. B. 0.05f)
        // und akzeptiere nur, wenn der aktuelle Ray knapp daneben ist. Hier lassen wir es bewusst weg.

        // 4) UI + Aktion
        string tag = hand_touch && hand_ob ? hand_ob.tag : "none";
        string action = use_action_and_ui_texter_set(tag);

        if (use_trigger)
        {
            use_trigger = false;
            if (action != "none" && hand_ob)
            {
                var interactable = hand_ob.GetComponentInParent<IInteractable>();
                if (interactable != null)
                {
                    if (debugInteraction)
                        Debug.Log($"[Interact-Call] -> {interactable.GetType().Name} on {hand_ob.name} with action '{action}'");
                    interactable.OnInteract(this.gameObject.name, action);
                }
                else if (debugInteraction)
                {
                    Debug.LogWarning($"[Interact-Miss] Kein IInteractable auf/über {hand_ob.name} gefunden.");
                }
            }
        }
    }
    void TryConsumeUse()
    {
        if (!use_trigger) return;

        string tag = hand_touch && hand_ob ? hand_ob.tag : "none";
        string action = use_action_and_ui_texter_set(tag);

        if (action != "none" && hand_ob)
        {
            use_trigger = false;
            var interactable = hand_ob.GetComponentInParent<IInteractable>();
            interactable?.OnInteract(this.gameObject.name, action);
        }
        else
        {
            use_trigger = false;
        }
    }

    string use_action_and_ui_texter_set(string object_tag)
    {
        string text_4_ui = "";
        string result_val = "";

        if (object_tag == "collectable")
        {
            text_4_ui = $"DRÜCKE {use_key_upper} ZUM EINSAMMELN";
            result_val = "collect";
            SetCrosshair(crosshair_positive);
            SetBGAlpha(0.4f);
        }
        else if (object_tag == "activatable")
        {
            text_4_ui = $"DRÜCKE {use_key_upper} ZUM INTERAGIEREN";
            result_val = "activate";
            SetCrosshair(crosshair_positive);
            SetBGAlpha(0.4f);
        }
        else if (object_tag != "none")
        {
            text_4_ui = "";
            result_val = "none";
            SetCrosshair(crosshair_negative);
            SetBGAlpha(0f);
        }
        else // none
        {
            text_4_ui = "";
            result_val = "none";
            SetCrosshair(crosshair_standard);
            SetBGAlpha(0f);
        }

        SetUIText(text_4_ui);
        return result_val;
    }

    void SetUIText(string text, bool force = false)
    {
        if (!hand_ui_texter) return;
        if (!force && cachedText == text) return;
        cachedText = text;
        hand_ui_texter.text = text;
    }

    void SetCrosshair(Sprite s)
    {
        if (!crosshair_ui || s == null) return;
        if (cachedCrosshair == s) return;
        cachedCrosshair = s;
        crosshair_ui.sprite = s;
    }

    void SetBGAlpha(float a, bool force = false)
    {
        if (!hand_ui_background) return;
        if (!force && Mathf.Approximately(cachedBGAlpha, a)) return;
        cachedBGAlpha = a;
        Color c = hand_ui_background.color;
        c.a = a;
        hand_ui_background.color = c;
    }

    void play_sound(AudioClip sound_file, float vol, bool random_pitch)
    {
        if (sounds_active && audio_source && sound_file)
        {
            audio_source.pitch = random_pitch ? Random.Range(0.7f, 1.0f) : 1.0f;
            audio_source.volume = vol;
            audio_source.loop = false;
            audio_source.PlayOneShot(sound_file, vol);
        }
    }

    void OnDrawGizmos()
    {
        if (cam == null) return;

        Gizmos.color = floor_touch ? Color.green : Color.white;
        Gizmos.DrawRay(transform.position, -transform.up * down_length);

        Gizmos.color = hand_touch ? Color.red : Color.white;
        Gizmos.DrawRay(cam.transform.position + cam.transform.forward * castStartOffset,
                       cam.transform.forward * hand_range);
    }
}
