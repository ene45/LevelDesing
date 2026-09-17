using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Platformer.Gameplay;
using static Platformer.Core.Simulation;
using Platformer.Model;
using Platformer.Core;

namespace Platformer.Mechanics
{
    /// <summary>
    /// This is the main class used to implement control of the player.
    /// It is a superset of the AnimationController class, but is inlined to allow for any kind of customisation.
    ///
    /// EXTENDED FOR LEVEL DESIGN CLASS:
    /// All movement/jump behaviour is exposed as public fields with [Header]/[Tooltip] so it can be
    /// tuned entirely from the Inspector: horizontal speed & acceleration, air control, double jump
    /// (on/off + amount + strength), coyote time and jump buffering.
    /// </summary>
    public class PlayerController : KinematicObject
    {
        [Tooltip("Sound played when the player jumps.")]
        public AudioClip jumpAudio;
        [Tooltip("Sound played when the player respawns.")]
        public AudioClip respawnAudio;
        [Tooltip("Sound played when the player takes damage.")]
        public AudioClip ouchAudio;

        // =========================================================
        //                  HORIZONTAL MOVEMENT
        // =========================================================
        [Header("Horizontal Movement")]
        [Tooltip("Maximum horizontal speed of the player. (Same as the original)")]
        public float maxSpeed = 7;

        [Tooltip("If DISABLED (default), movement is instant, matching the original behaviour. Enable it so the player accelerates/decelerates gradually instead of snapping to max speed instantly.")]
        public bool useSmoothAcceleration = false;

        [Tooltip("How fast the player accelerates up to maxSpeed (units/sec^2). Only applies if 'useSmoothAcceleration' is enabled.")]
        public float acceleration = 40f;

        [Tooltip("How fast the player decelerates when there is no horizontal input (units/sec^2). Only applies if 'useSmoothAcceleration' is enabled.")]
        public float deceleration = 45f;

        [Tooltip("Acceleration/deceleration multiplier while the player is airborne. 1 = same control as on the ground (default, matches the original). Only applies if 'useSmoothAcceleration' is enabled.")]
        [Range(0f, 1f)]
        public float airControlMultiplier = 1f;

        // internal value used to smooth horizontal movement frame to frame
        private float smoothedMoveX;

        // =========================================================
        //                     JUMP - BASIC
        // =========================================================
        [Header("Jump - Basic")]
        [Tooltip("Initial vertical velocity when jumping. Determines the height of a normal jump. (Same as the original)")]
        public float jumpTakeOffSpeed = 7;

        // =========================================================
        //                  JUMP - DOUBLE JUMP
        // =========================================================
        [Header("Jump - Double Jump")]
        [Tooltip("Enables or disables double jump (or multi-jump) while airborne. Disabled by default, matching the original.")]
        public bool enableDoubleJump = false;

        [Tooltip("Number of extra jumps allowed while the player is airborne (1 = double jump, 2 = triple jump, etc). Only applies if 'enableDoubleJump' is enabled.")]
        [Min(1)]
        public int extraJumpsAllowed = 1;

        [Tooltip("Speed multiplier for extra jumps relative to the normal jump (1 = same height as the normal jump).")]
        [Range(0.1f, 2f)]
        public float extraJumpSpeedMultiplier = 1f;

        private int jumpsRemaining;
        private bool doubleJumpRequested;

        // =========================================================
        //         JUMP - GAMEPLAY ASSISTS (GAME FEEL)
        // =========================================================
        [Header("Jump - Gameplay Assists")]
        [Tooltip("Grace period (in seconds) after leaving a platform during which the player can still jump (Coyote Time). 0 = disabled, matching the original.")]
        [Min(0f)]
        public float coyoteTime = 0f;

        [Tooltip("Time (in seconds) before landing during which a jump input is 'buffered' and executed as soon as the player lands (Jump Buffer). 0 = disabled, matching the original.")]
        [Min(0f)]
        public float jumpBufferTime = 0f;

        private float coyoteTimeCounter;
        private float jumpBufferCounter;
        private bool jumpPressedThisFrame;

        [Tooltip("Current state of the jump state machine. Read-only at runtime, exposed for debugging.")]
        public JumpState jumpState = JumpState.Grounded;
        private bool stopJump;
        [Tooltip("Reference to the player's Collider2D, used for movement and bounds calculations.")]
        /*internal new*/
        public Collider2D collider2d;
        [Tooltip("Reference to the player's AudioSource, used to play jump/respawn/ouch sounds.")]
        /*internal new*/
        public AudioSource audioSource;
        [Tooltip("Reference to the player's Health component.")]
        public Health health;
        [Tooltip("Whether the player currently responds to input. Disable to temporarily lock out player control (e.g. during cutscenes or death).")]
        public bool controlEnabled = true;

        bool jump;
        Vector2 move;
        SpriteRenderer spriteRenderer;
        internal Animator animator;
        readonly PlatformerModel model = Simulation.GetModel<PlatformerModel>();

        public Bounds Bounds => collider2d.bounds;

        void Awake()
        {
            health = GetComponent<Health>();
            audioSource = GetComponent<AudioSource>();
            collider2d = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            animator = GetComponent<Animator>();
            jumpsRemaining = extraJumpsAllowed;
        }

        protected override void Update()
        {
            jumpPressedThisFrame = false;

            if (controlEnabled)
            {
                move.x = Input.GetAxisRaw("Horizontal");

                if (Input.GetButtonDown("Jump"))
                {
                    jumpPressedThisFrame = true;
                    jumpBufferCounter = jumpBufferTime;
                }

                if (Input.GetButtonUp("Jump"))
                {
                    stopJump = true;
                    Schedule<PlayerStopJump>().player = this;
                }
            }
            else
            {
                move.x = 0;
            }

            // Coyote time: if set to 0 (default), this changes nothing compared to the original.
            if (IsGrounded)
            {
                coyoteTimeCounter = coyoteTime;
                jumpsRemaining = extraJumpsAllowed; // reset extra jumps when touching the ground
            }
            else
            {
                coyoteTimeCounter -= Time.deltaTime;
            }

            UpdateJumpState();
            base.Update();

            if (jumpBufferCounter > 0f)
                jumpBufferCounter -= Time.deltaTime;
        }

        void UpdateJumpState()
        {
            jump = false;

            // With jumpBufferTime = 0 and coyoteTime = 0 (default values), this behaves
            // exactly like "Input.GetButtonDown("Jump")" from the original script.
            bool wantsToJump = jumpPressedThisFrame || jumpBufferCounter > 0f;
            bool canGroundOrCoyoteJump = IsGrounded || coyoteTimeCounter > 0f;

            switch (jumpState)
            {
                case JumpState.Grounded:
                    if (wantsToJump && canGroundOrCoyoteJump)
                    {
                        jumpState = JumpState.PrepareToJump;
                        jumpBufferCounter = 0f;
                        coyoteTimeCounter = 0f;
                    }
                    break;
                case JumpState.PrepareToJump:
                    jumpState = JumpState.Jumping;
                    jump = true;
                    stopJump = false;
                    break;
                case JumpState.Jumping:
                    if (!IsGrounded)
                    {
                        Schedule<PlayerJumped>().player = this;
                        jumpState = JumpState.InFlight;
                    }
                    break;
                case JumpState.InFlight:
                    // Double jump: only kicks in if 'enableDoubleJump' is turned on (off by default).
                    if (enableDoubleJump && wantsToJump && jumpsRemaining > 0)
                    {
                        doubleJumpRequested = true;
                        jumpsRemaining--;
                        jumpBufferCounter = 0f;
                    }

                    if (IsGrounded)
                    {
                        Schedule<PlayerLanded>().player = this;
                        jumpState = JumpState.Landed;
                    }
                    break;
                case JumpState.Landed:
                    jumpState = JumpState.Grounded;
                    break;
            }
        }

        protected override void ComputeVelocity()
        {
            if (jump && IsGrounded)
            {
                velocity.y = jumpTakeOffSpeed * model.jumpModifier;
                jump = false;
            }
            else if (doubleJumpRequested)
            {
                velocity.y = jumpTakeOffSpeed * extraJumpSpeedMultiplier * model.jumpModifier;
                doubleJumpRequested = false;
                Schedule<PlayerJumped>().player = this;
                if (audioSource != null && jumpAudio != null)
                    audioSource.PlayOneShot(jumpAudio);
            }
            else if (stopJump)
            {
                stopJump = false;
                if (velocity.y > 0)
                {
                    velocity.y = velocity.y * model.jumpDeceleration;
                }
            }

            if (move.x > 0.01f)
                spriteRenderer.flipX = false;
            else if (move.x < -0.01f)
                spriteRenderer.flipX = true;

            animator.SetBool("grounded", IsGrounded);
            animator.SetFloat("velocityX", Mathf.Abs(velocity.x) / maxSpeed);

            if (!useSmoothAcceleration)
            {
                // Identical to the original script: instant velocity.
                targetVelocity = move * maxSpeed;
            }
            else
            {
                // --- Horizontal movement with acceleration/deceleration and air control ---
                float targetSpeed = move.x * maxSpeed;
                bool hasInput = Mathf.Abs(move.x) > 0.01f;
                float accelRate = hasInput ? acceleration : deceleration;
                if (!IsGrounded)
                    accelRate *= airControlMultiplier;

                smoothedMoveX = Mathf.MoveTowards(smoothedMoveX, targetSpeed, accelRate * Time.deltaTime);
                targetVelocity = new Vector2(smoothedMoveX, 0);
            }
        }

        public enum JumpState
        {
            Grounded,
            PrepareToJump,
            Jumping,
            InFlight,
            Landed
        }
    }
}