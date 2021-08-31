import sys
import json
import time
from enum import Enum
from pprint import pprint

import pygame
from pygame.locals import *
from pygame.math import Vector2
from twisted.internet.protocol import DatagramProtocol

from twisted.internet.task import LoopingCall
from twisted.internet import reactor

pygame.init()

# square window: 1000 x 1000
WINDOW_SIZE = 1000

# dimensions of the map image PNG files: 4096 x 4096
MAP_IMAGE_SIZE = 4096

MAP_IMAGE_OFFSET_X = 2.5
MAP_IMAGE_OFFSET_Y = 0

# how much the map image is bigger than the actual in game world (1750x2 -> 4096)
WORLD_TO_MAP_IMAGE_SCALE_X = 1.17269076305
WORLD_TO_MAP_IMAGE_SCALE_Y = 1.168

FPS = 30.0
MAX_ZOOM_FACTOR = 5.0
MIN_ZOOM_FACTOR = 0.75

# how long a game object lives until it's removed from the map, if there are no updates from the server
# GAME_OBJECT_TTL_SEC = 2.0

MARKER_SZE = 7.0
ICON_SIZE = 64
ICONS = {}

TYPE_PLAYER = 0
TYPE_ENEMY = 1
TYPE_CAVE_ENTRANCE = 2
TYPE_PICKUP = 3


class Item(Enum):
    CLOTH = 33
    MEDS = 49
    FLASHLIGHT = 51
    ROPE = 54
    OLD_POT = 142
    AIR_CANISTER = 144
    DYNAMITE = 175

    @classmethod
    def parse(cls, item_id: int):
        for item in Item:
            if item_id == item.value:
                return item
        return None


# convert in-game world coordinates (center at 0,0) to map image coordinates (normalized to 4096)
def world_to_map_image(wx: float, wy: float):
    mx = -(wx * WORLD_TO_MAP_IMAGE_SCALE_X) + MAP_IMAGE_SIZE / 2 - MAP_IMAGE_OFFSET_X
    my = (wy * WORLD_TO_MAP_IMAGE_SCALE_Y) + MAP_IMAGE_SIZE / 2 + MAP_IMAGE_OFFSET_Y
    return mx, my


def load_icons():
    ICONS[TYPE_CAVE_ENTRANCE] = pygame.image.load("icons/entrance.png")
    ICONS[Item.CLOTH] = pygame.image.load("icons/cloth.png")
    ICONS[Item.MEDS] = pygame.image.load("icons/meds.png")
    ICONS[Item.FLASHLIGHT] = pygame.image.load("icons/flashlight.png")
    ICONS[Item.ROPE] = pygame.image.load("icons/rope_pickup.png")
    ICONS[Item.OLD_POT] = pygame.image.load("icons/old_pot.png")
    ICONS[Item.AIR_CANISTER] = pygame.image.load("icons/air_canister.png")
    ICONS[Item.DYNAMITE] = pygame.image.load("icons/dynamite_64.png")


class Game(DatagramProtocol):
    isLeaf = True

    def __init__(self):
        super().__init__()

        pygame.display.set_caption('Forest Live Map')

        self.clock = pygame.time.Clock()
        self.screen_res = [WINDOW_SIZE, WINDOW_SIZE]
        self.screen = pygame.display.set_mode(self.screen_res, pygame.HWSURFACE, 32)

        self.zoom_factor = 2.5

        self.bg_world_image = pygame.image.load("overworld.jpg")
        self.bg_caves_image = pygame.image.load("caves.jpg")

        load_icons()

        # in-game coordinates of the point of view (map center)
        # by default, it will follow the player
        self.point_of_view_wx = 0.0
        self.point_of_view_wy = 0.0
        # self.point_of_view_wx = 350.0
        # self.point_of_view_wy = 1300.0

        # all game objects that are shown on the map (dict: instance ID -> json object)
        self.game_objects = {}
        # self.game_objects = {0: {"type": TYPE_PLAYER, "id": 0, "x": 350.0, "y": 1300.0, "rotZ": 110.0}}
        # self.game_objects = {0: {"type": TYPE_ROPE_PICKUP, "id": 0, "x": 100.0, "y": 100.0, "rotZ": 110.0}}

        # self.game_object_last_update_time = {}  # obj ID -> last update time in seconds (float)

        # flag that tells whether the player is in the caves
        self.is_in_caves = False
        self.is_running = True
        self.loop()

    # main loop method
    def loop(self):
        self.event_loop()
        # self.evict_expired_game_objects()
        self.draw()

    # process input events from mouse and keyboard
    def event_loop(self):
        for event in pygame.event.get():
            if event.type == QUIT:
                self.is_running = False
                tick.stop()
                reactor.stop()
                pygame.quit()
                sys.exit()
            if event.type == MOUSEWHEEL:
                self.zoom_factor -= event.y*0.1
                self.zoom_factor = min(self.zoom_factor, MAX_ZOOM_FACTOR)
                self.zoom_factor = max(self.zoom_factor, MIN_ZOOM_FACTOR)
            # if event.type == KEYDOWN:
            #     if event.key == pygame[K_ESCAPE]:
            #         pygame.quit()
            #         sys.exit()

    def remove_static_objects(self):
        for gid, go in list(self.game_objects.items()):
            if go['type'] == TYPE_CAVE_ENTRANCE or go['type'] == TYPE_PICKUP:
                del self.game_objects[gid]

    def remove_enemies(self):
        for gid, go in list(self.game_objects.items()):
            if go['type'] == TYPE_ENEMY:
                del self.game_objects[gid]

    def datagramReceived(self, data, addr):
        game_object = json.loads(data.decode('utf-8'))

        if "actionType" in game_object:
            self.remove_static_objects()
            return

        if game_object['type'] == TYPE_PLAYER and self.is_in_caves != game_object['inCave']:
            # clear entries on entering or leaving a cave
            self.remove_enemies()

        gid = game_object["id"]
        self.game_objects[gid] = game_object
        self.game_object_last_update_time[gid] = time.time()

        # follow the player on the map
        if game_object['type'] == TYPE_PLAYER:
            self.point_of_view_wx = game_object['x']
            self.point_of_view_wy = game_object['y']
            self.is_in_caves = game_object['inCave']
        if game_object['type'] == TYPE_PICKUP:
            game_object['item'] = Item.parse(game_object['itemID'])
            # pprint(game_object)

    def draw(self):
        if self.is_in_caves:
            map_surface = self.bg_caves_image.copy()
        else:
            map_surface = self.bg_world_image.copy()

        for game_object in list(self.game_objects.values()):
            if game_object['type'] == TYPE_PLAYER:
                self.draw_marker(map_surface, game_object['x'], game_object['y'], 'green', True, game_object['rotZ'])
                self.point_of_view_wx = game_object['x']
                self.point_of_view_wy = game_object['y']
            elif game_object['type'] == TYPE_ENEMY:
                self.draw_marker(map_surface, game_object['x'], game_object['y'], 'red', False, game_object['rotZ'])
            elif game_object['type'] == TYPE_CAVE_ENTRANCE:
                self.draw_marker_icon(map_surface, game_object['x'], game_object['y'], ICONS[TYPE_CAVE_ENTRANCE])
            elif "item" in game_object and game_object['item'] in ICONS:
                self.draw_marker_icon(map_surface, game_object['x'], game_object['y'], ICONS[game_object['item']])

        self.blit_map_surface(map_surface)
        pygame.display.update()

    def draw_marker(self, map_surface, wx: float, wy: float, color: str, show_direction: bool, rot_z: float):
        mx, my = world_to_map_image(wx, wy)
        pygame.draw.circle(map_surface, pygame.color.Color(color), (mx, my), (MARKER_SZE - 1) * self.zoom_factor - 1)
        pygame.draw.circle(map_surface, pygame.color.Color('white'), (mx, my), MARKER_SZE * self.zoom_factor, width=1)

        if show_direction:
            v = Vector2(0, (MARKER_SZE + 5) * self.zoom_factor)
            v.rotate_ip(rot_z)
            arrow_cx, arrow_cy = (mx + v.x, my + v.y)
            v.scale_to_length((MARKER_SZE + 2) * self.zoom_factor)
            v.rotate_ip(-45)
            arrow_lx, arrow_ly = (mx + v.x, my + v.y)
            v.rotate_ip(90)
            arrow_rx, arrow_ry = (mx + v.x, my + v.y)
            pygame.draw.line(map_surface, pygame.color.Color('white'), (arrow_cx, arrow_cy), (arrow_lx, arrow_ly), 2)
            pygame.draw.line(map_surface, pygame.color.Color('white'), (arrow_cx, arrow_cy), (arrow_rx, arrow_ry), 2)
            pass

    def draw_marker_icon(self, map_surface, wx: float, wy: float, marker_image):
        mx, my = world_to_map_image(wx, wy)
        scaled_icon_size = ICON_SIZE * self.zoom_factor / 4
        marker_image = pygame.transform.smoothscale(marker_image, (int(scaled_icon_size), int(scaled_icon_size)))
        map_surface.blit(marker_image, (mx - scaled_icon_size/2, my - scaled_icon_size/2))

    def blit_map_surface(self, map_surface):
        dst_size = (int(map_surface.get_rect().w / self.zoom_factor), int(map_surface.get_rect().h / self.zoom_factor))
        map_surface = pygame.transform.smoothscale(map_surface, dst_size)
        rect = map_surface.get_rect()
        rect.center = WINDOW_SIZE / 2, WINDOW_SIZE / 2

        pov_mx, pov_my = world_to_map_image(self.point_of_view_wx, self.point_of_view_wy)
        center_offset_x = (pov_mx - MAP_IMAGE_SIZE / 2) / self.zoom_factor
        center_offset_y = (pov_my - MAP_IMAGE_SIZE / 2) / self.zoom_factor
        rect.move_ip(-center_offset_x, -center_offset_y)

        self.screen.fill(pygame.color.Color('black'))
        self.screen.blit(map_surface, rect)


game = Game()

tick = LoopingCall(game.loop)
tick.start(1.0 / FPS)
reactor.listenUDP(9999, game)
reactor.run()
