﻿using System.Linq.Expressions;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using SmartMass.Controller.Api.Data;
using SmartMass.Controller.Model.DTOs;
using SmartMass.Controller.Model.Mapping;
using SmartMass.Controller.Model.PageModels;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace SmartMass.Controller.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DevicesController : ControllerBase
    {
        private readonly SmartMassDbContext dbContext;
        private readonly ILogger<DevicesController> logger;
        private readonly Mqtt.IMqttClient mqttClient;
        private readonly string mqttTopicBase = string.Empty;

        public DevicesController(ILogger<DevicesController> logger, IConfiguration config, SmartMassDbContext dbContext, Mqtt.IMqttClient mqttClient)
        {
            this.logger = logger;
            this.dbContext = dbContext;
            this.mqttClient = mqttClient;
            this.mqttTopicBase = config.GetValue<string>("mqtt:topic");
        }

        // GET: api/<DevicesController>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public ActionResult<IEnumerable<Device>> Get()
        {
            if (dbContext.Devices != null)
            {
                return dbContext.Devices.Select(device => device.MapTo()).ToList();
            }

            return Problem("no context");
        }

        // GET api/<DevicesController>/5
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<Device>> Get(int id)
        {
            if (dbContext.Devices != null)
            {
                var device = await dbContext.Devices.FindAsync(id);
                if (device != null)
                {
                    return device.MapTo();
                }
                else return NotFound();
            }
            else return Problem("no context");
        }

        // POST api/<DevicesController>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Create([FromBody] Device device)
        {
            if (ModelState.IsValid)
            {
                var dto = new DeviceDTO();
                dto.CreateFrom(device);
                dbContext.Add(dto);
                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    return Problem(e.Message);
                }

                return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
            }
            else return BadRequest(ModelState);
        }

        [HttpPost("{id}/configure")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Configure(int id)
        {
            try
            {
                var dto = await dbContext.Devices.FindAsync(id);
                if (dto == null) return NotFound();

                dynamic dynObj = new
                {
                    action = "configure",
                    scale = new
                    {
                        update_interval = dto.ScaleUpdateInterval,
                        sampling_size = dto.ScaleSamplingSize,
                        calibration = dto.CalibrationFactor,
                        known_weight = dto.ScaleCalibrationWeight
                    },
                    display = new
                    {
                        display_timeout = dto.ScaleDisplayTimeout
                    }
                };

                mqttClient.Publish(BuildMqttTopic(this.mqttTopicBase, dto.ClientId), JsonConvert.SerializeObject(dynObj));
                return Ok();
            }
            catch (Exception e)
            {
                return Problem(e.Message);
            }
        }

        [HttpPost("{id}/tare")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Tare(int id)
        {
            try
            {
                var dto = await dbContext.Devices.FindAsync(id);
                if (dto == null) return NotFound();

                dynamic dynObj = new
                {
                    action = "tare"
                };

                mqttClient.Publish(BuildMqttTopic(this.mqttTopicBase, dto.ClientId), JsonConvert.SerializeObject(dynObj));
                return Ok();
            }
            catch (Exception e)
            {
                return Problem(e.Message);
            }
        }

        [HttpPost("{id}/calibrate")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Calibrate(int id)
        {
            try
            {
                var dto = await dbContext.Devices.FindAsync(id);
                if (dto == null) return NotFound();

                dynamic dynObj = new
                {
                    action = "calibrate"
                };

                mqttClient.Publish(BuildMqttTopic(this.mqttTopicBase, dto.ClientId), Convert.ToString(dynObj));
                return Ok();
            }
            catch (Exception e)
            {
                return Problem(e.Message);
            }
        }

        // PUT api/<DevicesController>/5
        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Put(int id, [FromBody] Device device)
        {
            if(id != device.Id) return BadRequest();

            if (ModelState.IsValid)
            {
                var dto = new DeviceDTO();
                dto.MapFrom(device);

                this.dbContext.Entry(dto).State = EntityState.Modified;
                try
                {
                    await dbContext.SaveChangesAsync();
                }
                catch (DbUpdateException e)
                {
                    if (DeviceExists(id))
                    {
                        return NotFound();
                    }
                    else
                    {
                        return Problem(e.Message);
                    }
                }

                return NoContent();
            }
            else return BadRequest(ModelState);
        }

        // DELETE api/<DevicesController>/5
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await this.dbContext.Devices.FindAsync(id);
            if (item != null)
            {
                this.dbContext.Devices.Remove(item);
                try
                {
                    await this.dbContext.SaveChangesAsync();
                }
                catch (Exception e)
                {
                    return Problem(e.Message);
                }

                return NoContent();
            }
            else return NotFound();
        }

        private bool DeviceExists(long id)
        {
            return this.dbContext.Devices.Any(e => e.Id == id);
        }

        private string BuildMqttTopic(string baseTopic, string subTopic)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(baseTopic))
            {
                sb.Append(baseTopic);
                if (!baseTopic.EndsWith('/'))
                {
                    sb.Append('/');
                }
            }

            if (!string.IsNullOrWhiteSpace(subTopic))
            {
                sb.Append(subTopic);
                //if (!subTopic.EndsWith('/'))
                //{
                //    sb.Append('/');
                //}
            }
            //TODO: remove before prod :D
            else
            {
                sb.Append("scale-01");
            }

            return sb.ToString();
        }
    }
}
